using System.Runtime.InteropServices;
using PrivacyMask.Updates;

var applyApplicationExited = false;
try
{
    var self = Environment.ProcessPath ?? throw new InvalidOperationException("Missing launcher path.");
    if (UpdateApplyRequest.IsRequested(args))
    {
        var verifier = UpdateRuntimePolicy.CreateVerifier();
        using var discovery = new GitHubReleaseClient(verifier);
        var result = await LauncherApply.RunAsync(self, args, verifier, discovery,
            applicationExited: () => applyApplicationExited = true);
        if (result == LauncherApplyOutcome.PreviousVersionRestarted)
            _ = NativeMessage.Show(nint.Zero,
                "The update was not installed. Your previous PrivacyMask version has been restored and restarted.",
                "PrivacyMask update", 0x30);
        return 0;
    }
    using var child = LauncherBoot.Launch(self, args);
    return 0;
}
catch
{
    if (UpdateApplyRequest.IsRequested(args))
    {
        // A live App owns pre-exit feedback/cancellation. Do not strand its worker behind a modal.
        if (!applyApplicationExited) return 21;
        _ = NativeMessage.Show(nint.Zero,
            "The update could not finish. If PrivacyMask is still running, keep using it. Otherwise close sensitive windows and start PrivacyMask again. Run the installer if repair is required.",
            "PrivacyMask update stopped", 0x30);
        return 21;
    }
    _ = NativeMessage.Show(nint.Zero,
        "PrivacyMask could not verify or start this installation. Run the installer again to repair it.",
        "PrivacyMask needs repair", 0x10);
    return 20;
}

internal static partial class NativeMessage
{
    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int Show(nint window, string text, string caption, uint type);
}
