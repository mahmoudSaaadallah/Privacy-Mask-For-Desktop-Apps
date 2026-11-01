function Get-PrivacyMaskStartupMatch {
  param([object]$Value, [string[]]$ExecutablePaths)
  if ($Value -is [string]) {
    $command = $Value.Trim()
    foreach ($path in $ExecutablePaths) {
      if ([string]::IsNullOrWhiteSpace($path)) { continue }
      $quoted = '"' + $path + '"'
      if ([string]::Equals($command, $quoted, [StringComparison]::OrdinalIgnoreCase)) {
        return [pscustomobject]@{ Matches = $true; StartMinimized = $false }
      }
      if ([string]::Equals($command, $quoted + ' --minimized', [StringComparison]::OrdinalIgnoreCase)) {
        return [pscustomobject]@{ Matches = $true; StartMinimized = $true }
      }
    }
  }
  return [pscustomobject]@{ Matches = $false; StartMinimized = $false }
}

function Get-PrivacyMaskStartupMigration {
  param([object]$Value, [string[]]$ExecutablePaths, [string]$LauncherPath)
  $match = Get-PrivacyMaskStartupMatch -Value $Value -ExecutablePaths $ExecutablePaths
  if (-not $match.Matches) { return $null }
  $desired = '"' + $LauncherPath + '"'
  if ($match.StartMinimized) { $desired += ' --minimized' }
  if ([string]::Equals($Value, $desired, [StringComparison]::Ordinal)) { return $null }
  return $desired
}

if (-not ('PrivacyMask.Installer.UnicodeShortcut' -as [type])) {
  Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace PrivacyMask.Installer
{
    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    internal class ShellLink
    {
    }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellLinkW
    {
        void GetPath(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder filePath,
            int filePathLength,
            out Win32FindData findData,
            uint flags);

        void GetIDList(out IntPtr itemIdList);
        void SetIDList(IntPtr itemIdList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int descriptionLength);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder workingDirectory, int workingDirectoryLength);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string workingDirectory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int argumentsLength);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int iconPathLength, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
        void Resolve(IntPtr windowHandle, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string filePath);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct Win32FindData
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint Reserved0;
        public uint Reserved1;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string FileName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string AlternateFileName;
    }

    public static class UnicodeShortcut
    {
        public static void Create(string shortcutPath, string targetPath, string workingDirectory, string description)
        {
            IShellLinkW shellLink = (IShellLinkW)new ShellLink();

            try
            {
                shellLink.SetPath(targetPath);
                shellLink.SetWorkingDirectory(workingDirectory);
                shellLink.SetDescription(description);
                shellLink.SetIconLocation(targetPath, 0);
                ((IPersistFile)shellLink).Save(shortcutPath, true);
            }
            finally
            {
                Marshal.FinalReleaseComObject(shellLink);
            }
        }

        public static string ReadTargetPath(string shortcutPath)
        {
            IShellLinkW shellLink = (IShellLinkW)new ShellLink();

            try
            {
                ((IPersistFile)shellLink).Load(shortcutPath, 0);
                StringBuilder targetPath = new StringBuilder(32768);
                Win32FindData findData;
                shellLink.GetPath(targetPath, targetPath.Capacity, out findData, 0);
                return targetPath.ToString();
            }
            finally
            {
                Marshal.FinalReleaseComObject(shellLink);
            }
        }

        public static string ReadIconLocation(string shortcutPath)
        {
            IShellLinkW shellLink = (IShellLinkW)new ShellLink();
            try
            {
                ((IPersistFile)shellLink).Load(shortcutPath, 0);
                StringBuilder iconPath = new StringBuilder(32768);
                int iconIndex;
                shellLink.GetIconLocation(iconPath, iconPath.Capacity, out iconIndex);
                return iconPath.ToString() + "," + iconIndex;
            }
            finally
            {
                Marshal.FinalReleaseComObject(shellLink);
            }
        }
    }
}
'@
}

function New-PrivacyMaskShortcut {
  param(
    [Parameter(Mandatory = $true)]
    [string]$ShortcutPath,

    [Parameter(Mandatory = $true)]
    [string]$TargetPath,

    [Parameter(Mandatory = $true)]
    [string]$WorkingDirectory,

    [Parameter(Mandatory = $true)]
    [string]$Description
  )

  [PrivacyMask.Installer.UnicodeShortcut]::Create(
    $ShortcutPath,
    $TargetPath,
    $WorkingDirectory,
    $Description)
}
