using System.IO;
using System.Text.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using PrivacyMask.App.Services;
using PrivacyMask.Core.Models;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class UpdateCheckHistoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CadenceCountsAttemptsNotSuccessAndPreservesFutureClocks()
    {
        Assert.True(new UpdateCheckHistory().StartupDue(Now));
        var attempted = new UpdateCheckHistory().Attempt(Now).Complete(UpdateChannel.Preview,
            new(UpdateCheckStatus.UnableToCheck), Now);
        Assert.False(new UpdateCheckHistory().Attempt(Now).StartupDue(Now.AddYears(1)));
        Assert.False(attempted.StartupDue(Now.AddHours(23)));
        Assert.True(attempted.StartupDue(Now.AddHours(24)));
        Assert.False(attempted.StartupDue(Now.AddDays(-1)));
        Assert.Equal(Now, attempted.Attempt(Now.AddDays(-1)).LastAttemptUtc);
    }

    [Fact]
    public void RetryIsGlobalAndFailuresDoNotAdvanceSuccess()
    {
        var history = new UpdateCheckHistory().Attempt(Now).Complete(UpdateChannel.Preview,
            new(UpdateCheckStatus.RateLimited, RetryAt: Now.AddDays(2)), Now);
        Assert.False(history.StartupDue(Now.AddDays(1)));
        Assert.Null(history.PreviewSuccessUtc);
        history = history.Complete(UpdateChannel.Stable, new(UpdateCheckStatus.UpToDate), Now);
        Assert.Equal(Now, history.StableSuccessUtc);
        Assert.Equal(Now.AddDays(2), history.RetryAfterUtc);
    }

    [Fact]
    public async Task AtomicRoundTripIsPrivateBoundedAndDoesNotTouchSettings()
    {
        using var fixture = new RecoveryFixture();
        var settings = File.ReadAllBytes(fixture.Primary);
        var store = new UpdateCheckHistoryStore(fixture.Root);
        Assert.Equal(new(), await store.ReadAsync(default));
        var expected = new UpdateCheckHistory().Attempt(Now).Complete(UpdateChannel.Preview, new(UpdateCheckStatus.UpToDate), Now);
        await store.WriteAsync(expected, default);
        await store.WriteAsync(expected with { RetryAfterUtc = Now.AddDays(1) }, default);
        var saved = await new UpdateCheckHistoryStore(fixture.Root).ReadAsync(default);
        Assert.Equal(expected with { RetryAfterUtc = Now.AddDays(1) }, saved);
        var path = Path.Combine(fixture.Root, UpdateCheckHistoryStore.FileName);
        Assert.InRange(new FileInfo(path).Length, 1, UpdateCheckHistoryStore.MaximumBytes);
        ManagedInstallLease.AssertPrivate(path);
        Assert.False(File.Exists(Path.Combine(fixture.Root, ".update-check-history.tmp")));
        Assert.Equal(settings, File.ReadAllBytes(fixture.Primary));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1}")]
    [InlineData("[]")]
    public async Task MalformedHistoryIsPreserved(string json)
    {
        using var fixture = new RecoveryFixture();
        var path = Path.Combine(fixture.Root, UpdateCheckHistoryStore.FileName);
        using (var stream = ManagedInstallLease.CreatePrivateFile(path))
            stream.Write(System.Text.Encoding.UTF8.GetBytes(json));
        await Assert.ThrowsAnyAsync<Exception>(() => new UpdateCheckHistoryStore(fixture.Root).ReadAsync(default));
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public async Task ScratchCollisionIsPreservedAndCannotResetThrottle()
    {
        using var fixture = new RecoveryFixture();
        var path = Path.Combine(fixture.Root, ".update-check-history.tmp");
        using (var stream = ManagedInstallLease.CreatePrivateFile(path)) stream.WriteByte(42);
        var store = new UpdateCheckHistoryStore(fixture.Root);
        await Assert.ThrowsAsync<IOException>(() => store.ReadAsync(default));
        await Assert.ThrowsAsync<IOException>(() => store.WriteAsync(new(), default));
        Assert.Equal(new byte[] { 42 }, File.ReadAllBytes(path));
    }

    [Fact]
    public void SeparateSessionsCannotAcquireConcurrentHistoryAuthority()
    {
        using var fixture = new RecoveryFixture();
        var first = new UpdateCheckHistoryStore(fixture.Root);
        var second = new UpdateCheckHistoryStore(fixture.Root);
        using (first.Acquire()) Assert.ThrowsAny<Exception>(() => second.Acquire());
        using (second.Acquire()) Assert.Equal(0, new FileInfo(Path.Combine(fixture.Root, ".update-check.lock")).Length);
    }

    [Fact]
    public async Task OversizedHistoryIsNotBufferedOrReplaced()
    {
        using var fixture = new RecoveryFixture();
        var path = Path.Combine(fixture.Root, UpdateCheckHistoryStore.FileName);
        using (var file = ManagedInstallLease.CreatePrivateFile(path)) file.Write(new byte[UpdateCheckHistoryStore.MaximumBytes + 1]);
        await Assert.ThrowsAsync<IOException>(() => new UpdateCheckHistoryStore(fixture.Root).ReadAsync(default));
        Assert.Equal(UpdateCheckHistoryStore.MaximumBytes + 1, new FileInfo(path).Length);
    }

    [Fact]
    public async Task LockedHistoryRemainsIntact()
    {
        using var fixture = new RecoveryFixture();
        var store = new UpdateCheckHistoryStore(fixture.Root);
        var expected = new UpdateCheckHistory().Attempt(Now).Complete(UpdateChannel.Stable, new(UpdateCheckStatus.UpToDate), Now);
        await store.WriteAsync(expected, default);
        var path = Path.Combine(fixture.Root, UpdateCheckHistoryStore.FileName);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Assert.ThrowsAnyAsync<Exception>(() => store.ReadAsync(default));
        Assert.Equal(expected, await store.ReadAsync(default));
    }

    [Fact]
    public async Task ForeignAccessIsRejectedWithoutChangingPermissionsOrContent()
    {
        using var fixture = new RecoveryFixture();
        var store = new UpdateCheckHistoryStore(fixture.Root);
        await store.WriteAsync(new(), default);
        var file = new FileInfo(Path.Combine(fixture.Root, UpdateCheckHistoryStore.FileName));
        var content = File.ReadAllBytes(file.FullName);
        var security = file.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadData, AccessControlType.Allow));
        file.SetAccessControl(security);
        await Assert.ThrowsAnyAsync<Exception>(() => store.ReadAsync(default));
        Assert.Equal(content, File.ReadAllBytes(file.FullName));
        Assert.ThrowsAny<Exception>(() => ManagedInstallLease.AssertPrivate(file.FullName));
    }
}
