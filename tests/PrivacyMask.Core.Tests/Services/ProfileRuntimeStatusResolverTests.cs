using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class ProfileRuntimeStatusResolverTests
{
    [Fact]
    public void Resolve_ReportsDisabledBeforeInspectingRuntimeState()
    {
        var profile = CreateProfile();
        profile.Enabled = false;

        var status = Resolve(profile, running: true, protectedNow: true);

        Assert.Equal(ProfileRuntimeStatus.Disabled, status);
    }

    [Fact]
    public void Resolve_DistinguishesStoppedAndMissingTargets()
    {
        var withPath = CreateProfile("C:\\Apps\\Notes.exe");
        var legacy = CreateProfile();

        Assert.Equal(
            ProfileRuntimeStatus.TargetMissing,
            Resolve(withPath, running: false, protectedNow: false, executableExists: false));
        Assert.Equal(
            ProfileRuntimeStatus.NotRunning,
            Resolve(legacy, running: false, protectedNow: false, executableExists: false));
    }

    [Theory]
    [InlineData(RuntimeMode.Standard, false, false, true, ProfileRuntimeStatus.Protected)]
    [InlineData(RuntimeMode.Standard, false, false, false, ProfileRuntimeStatus.WaitingForFocus)]
    [InlineData(RuntimeMode.Off, false, false, false, ProfileRuntimeStatus.ProtectionPaused)]
    [InlineData(RuntimeMode.Standard, true, false, false, ProfileRuntimeStatus.ProtectionPaused)]
    [InlineData(RuntimeMode.Panic, false, false, true, ProfileRuntimeStatus.PanicProtected)]
    [InlineData(RuntimeMode.Standard, false, true, true, ProfileRuntimeStatus.PanicProtected)]
    public void Resolve_ReportsActiveRuntimeState(
        RuntimeMode globalMode,
        bool profilePaused,
        bool profilePanic,
        bool protectedNow,
        ProfileRuntimeStatus expected)
    {
        var status = Resolve(
            CreateProfile(),
            running: true,
            protectedNow,
            globalMode,
            profilePaused,
            profilePanic);

        Assert.Equal(expected, status);
    }

    private static ProfileRuntimeStatus Resolve(
        AppProfile profile,
        bool running,
        bool protectedNow,
        RuntimeMode globalMode = RuntimeMode.Standard,
        bool profilePaused = false,
        bool profilePanic = false,
        bool executableExists = true)
    {
        IReadOnlySet<string> runningNames = running
            ? new HashSet<string>(["Notes"], StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IReadOnlySet<string> protectedIds = protectedNow
            ? new HashSet<string>([profile.ProfileId], StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return ProfileRuntimeStatusResolver.Resolve(
            profile,
            runningNames,
            protectedIds,
            globalMode,
            profilePaused,
            profilePanic,
            executableExists);
    }

    private static AppProfile CreateProfile(string? executablePath = null)
    {
        return CustomAppProfileFactory.Create(
            "Notes",
            "Notes",
            "custom-notes",
            executablePath);
    }
}
