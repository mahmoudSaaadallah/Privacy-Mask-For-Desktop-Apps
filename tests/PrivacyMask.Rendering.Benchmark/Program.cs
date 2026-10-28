using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PrivacyMask.App.Windows;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using WpfApplication = System.Windows.Application;
using Point = System.Windows.Point;

internal static class Program
{
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }

    [STAThread]
    private static int Main(string[] args)
    {
        var scenario = "";
        var seconds = 0;
        var admitted = false;
        for (var i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length) return 2;
            if (args[i] == "--scenario" && args[i + 1] is "IdleFixture" or "OneWindow" or "TwoWindows" or "MoveResize" or "RevealInputs") scenario = args[i + 1];
            else if (args[i] == "--duration-seconds" && int.TryParse(args[i + 1], out var duration) && duration is >= 5 and <= 3600) seconds = duration;
            else if (args[i] == "--allow-visible-fixtures" && args[i + 1] == "true") admitted = true;
            else return 2;
        }
        if (!admitted || scenario.Length == 0 || seconds == 0) return 2;
        Forms.Application.SetHighDpiMode(Forms.HighDpiMode.PerMonitorV2);
        var application = new WpfApplication { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var forms = new List<SyntheticForm>();
        var overlays = new List<PrivacyOverlayWindow>();
        var profiles = new List<AppProfile>();
        var lastFrames = new BitmapSource?[scenario == "TwoWindows" ? 2 : 1];
        var frameCounts = new long[lastFrames.Length];
        var lastSeen = new double[lastFrames.Length];
        var pixelVariation = new bool[lastFrames.Length];
        var dimensions = new string[lastFrames.Length];
        var elapsed = Stopwatch.StartNew();
        var ready = false;
        var stopping = false;
        var aborted = false;
        var mode = RuntimeMode.Standard;
        var timer = new DispatcherTimer { Interval = OverlayRefreshCadence.ActiveInterval };
        void Stop(bool failed)
        {
            if (stopping) return;
            stopping = true;
            aborted |= failed;
            timer.Stop();
            application.Shutdown();
        }
        object Status(string state) => new
        {
            State = state, EvidenceKind = "LiveRendererWithOwnedSyntheticTargets", Scenario = scenario,
            ProductVersion = typeof(PrivacyOverlayWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
            WindowCount = forms.Count, FrameCounts = frameCounts, PixelVariation = pixelVariation,
            TargetSizes = forms.Select(form => $"{form.Width}x{form.Height}").ToArray(),
            MaskStyle = "Blur", Strength = 0.7, RendererTickMilliseconds = OverlayRefreshCadence.ActiveInterval.TotalMilliseconds,
            FrameDimensions = dimensions, LastFrameAgeSeconds = lastSeen.Select(value => Math.Round(elapsed.Elapsed.TotalSeconds - value, 2)).ToArray(),
            ElapsedSeconds = Math.Round(elapsed.Elapsed.TotalSeconds, 2), Aborted = aborted,
            FullProductionShellStarted = false, RevealParametersSimulated = scenario == "RevealInputs",
            CaptureScope = "Only handles created by this process; no settings/hotkeys/startup/network",
        };
        try
        {
            var area = Forms.Screen.PrimaryScreen!.WorkingArea;
            var width = Math.Min(900, Math.Max(300, (area.Width - 80) / lastFrames.Length));
            var height = Math.Min(700, Math.Max(250, area.Height - 180));
            for (var i = 0; i < lastFrames.Length; i++)
            {
                var form = new SyntheticForm
                {
                    Text = $"PrivacyMask synthetic target {i + 1} — close to stop test",
                    StartPosition = Forms.FormStartPosition.Manual, AutoScaleMode = Forms.AutoScaleMode.None,
                    Bounds = new Drawing.Rectangle(area.Left + 20 + i * (width + 20), area.Top + 50, width, height),
                    TopMost = true,
                };
                form.FormClosed += (_, _) => Stop(true);
                form.Show();
                forms.Add(form);
                overlays.Add(new PrivacyOverlayWindow { Title = "PrivacyMask synthetic renderer only" });
                var profile = CustomAppProfileFactory.Create("Synthetic only", "PrivacyMask.Rendering.Benchmark", $"fixture-{i}");
                profile.MaskIntensity = 0.7;
                profile.Zones[0].Strength = 0.7;
                profiles.Add(profile);
            }
            timer.Tick += (_, _) =>
            {
                try
                {
                    var t = elapsed.Elapsed.TotalSeconds;
                    for (var i = 0; i < forms.Count; i++)
                    {
                        var form = forms[i];
                        if (form.IsDisposed) { Stop(true); return; }
                        if (scenario == "MoveResize")
                            form.SetBounds(area.Left + 80 + (int)(40 * Math.Sin(t)), area.Top + 80 + (int)(25 * Math.Cos(t)),
                                width - 50 + (int)(30 * Math.Sin(t)), height - 40 + (int)(20 * Math.Cos(t)));
                        GetWindowThreadProcessId(form.Handle, out var owner);
                        if (owner != Environment.ProcessId || !GetWindowRect(form.Handle, out var rect)) { Stop(true); return; }
                        var bounds = new ScreenRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
                        var reveal = scenario == "RevealInputs" && (int)t % 4 >= 2;
                        var cursor = scenario == "RevealInputs" ? new Point(bounds.Left + bounds.Width * (0.5 + 0.4 * Math.Sin(t)), bounds.Top + bounds.Height / 2d) : new Point(-10000, -10000);
                        overlays[i].UpdateOverlay(new TrackedWindow
                        {
                            Profile = profiles[i], Preset = profiles[i].Presets[0], EffectiveZones = profiles[i].Zones,
                            Snapshot = new WindowSnapshot { Handle = form.Handle, Bounds = bounds, IsVisible = true, IsForeground = true, ProcessName = "PrivacyMask.Rendering.Benchmark" },
                        }, scenario == "IdleFixture" ? RuntimeMode.Off : mode, reveal, cursor);
                        if (overlays[i].Content is Canvas canvas)
                        {
                            var frame = canvas.Children.OfType<System.Windows.Controls.Image>().Select(image => image.Source).OfType<BitmapSource>().FirstOrDefault();
                            if (frame is not null && !ReferenceEquals(frame, lastFrames[i]))
                            {
                                lastFrames[i] = frame;
                                frameCounts[i]++;
                                lastSeen[i] = t;
                                dimensions[i] = $"{frame.PixelWidth}x{frame.PixelHeight}";
                                if (!pixelVariation[i]) pixelVariation[i] = HasVariation(frame);
                            }
                        }
                    }
                    if (!ready && (scenario == "IdleFixture" || pixelVariation.All(value => value)))
                    {
                        ready = true;
                        Console.WriteLine(JsonSerializer.Serialize(Status("READY")));
                    }
                    if ((!ready && t > 20) || t > seconds) Stop(!ready);
                }
                catch { Stop(true); }
            };
            _ = Task.Run(() =>
            {
                string? command;
                while ((command = Console.ReadLine()) is not null)
                {
                    var current = command;
                    application.Dispatcher.BeginInvoke(() =>
                    {
                        if (current == "status") Console.WriteLine(JsonSerializer.Serialize(Status("STATUS")));
                        else if (current == "stop") Stop(false);
                    });
                    if (current == "stop") return;
                }
                application.Dispatcher.BeginInvoke(() => Stop(true));
            });
            timer.Start();
            application.Run();
        }
        finally
        {
            stopping = true;
            timer.Stop();
            foreach (var overlay in overlays) { overlay.HideOverlay(); overlay.Close(); }
            foreach (var form in forms) form.Dispose();
        }
        Console.WriteLine(JsonSerializer.Serialize(Status("FINISHED")));
        return ready && !aborted ? 0 : 1;
    }

    private static bool HasVariation(BitmapSource frame)
    {
        if (frame.Format.BitsPerPixel != 32) return false;
        var pixel = new byte[4];
        var colors = new HashSet<int>();
        for (var y = 1; y < 8; y++)
            for (var x = 1; x < 8; x++)
            {
                frame.CopyPixels(new Int32Rect(x * (frame.PixelWidth - 1) / 8, y * (frame.PixelHeight - 1) / 8, 1, 1), pixel, 4, 0);
                colors.Add(pixel[0] | pixel[1] << 8 | pixel[2] << 16);
            }
        return colors.Count >= 3;
    }

    private sealed class SyntheticForm : Forms.Form
    {
        protected override bool ShowWithoutActivation => true;
        protected override void OnPaint(Forms.PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(Drawing.Color.White);
            e.Graphics.FillRectangle(Drawing.Brushes.Teal, 0, 0, ClientSize.Width, 100);
            for (var y = 120; y < ClientSize.Height - 25; y += 35)
            {
                e.Graphics.FillRectangle(y % 2 == 0 ? Drawing.Brushes.Orange : Drawing.Brushes.SkyBlue, 20, y, 90, 24);
                e.Graphics.DrawString("SYNTHETIC TEST CONTENT — no personal information", Font, Drawing.Brushes.Navy, 130, y);
            }
        }
    }
}
