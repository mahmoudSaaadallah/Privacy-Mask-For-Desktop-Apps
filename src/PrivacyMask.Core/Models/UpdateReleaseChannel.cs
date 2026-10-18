using System.Reflection;

namespace PrivacyMask.Core.Models;

public enum UpdateReleaseChannel { Stable, Preview }

public static class UpdatePreferenceDefaults
{
    public static UpdateReleaseChannel Channel { get; } = ForVersion(
        typeof(UpdatePreferenceDefaults).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "");

    public static UpdateReleaseChannel ForVersion(string version) => version.Split('+')[0].Contains('-')
        ? UpdateReleaseChannel.Preview : UpdateReleaseChannel.Stable;
}
