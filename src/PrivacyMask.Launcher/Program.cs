using System.Runtime.InteropServices;
using PrivacyMask.Updates;

try
{
    var self = Environment.ProcessPath ?? throw new InvalidOperationException("Missing launcher path.");
    using var child = LauncherBoot.Launch(self, args);
    return 0;
}
catch
{
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
