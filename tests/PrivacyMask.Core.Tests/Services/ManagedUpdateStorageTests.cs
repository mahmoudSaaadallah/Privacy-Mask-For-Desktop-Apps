using System.IO;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class ManagedUpdateStorageTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task Retention_preserves_independent_absence_and_empty_settings_in_the_retained_pair(bool primary, bool backup, bool empty)
    {
        using var fixture = new RetentionFixture(primary, backup, empty);
        var keep = fixture.Archive("2.0.0-beta.3", "2.0.0-beta.4", installedTarget: true);
        var old = fixture.Archive("2.0.0-beta.2", "2.0.0-beta.3");
        Assert.Equal(1, (await fixture.Run()).RemovedDirectories);
        Assert.True(Directory.Exists(keep));
        Assert.False(Directory.Exists(old));
        Assert.Equal(primary, File.Exists(Path.Combine(keep, "original-settings.json")));
        Assert.Equal(backup, File.Exists(Path.Combine(keep, "original-settings.bak")));
        if (primary && empty) Assert.Equal(0, new FileInfo(Path.Combine(keep, "original-settings.json")).Length);
        if (backup && empty) Assert.Equal(0, new FileInfo(Path.Combine(keep, "original-settings.bak")).Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Managed_stage_capacity_or_pending_journal_reject_before_network(bool pending)
    {
        using var fixture = new RetentionFixture();
        using var trust = new UpdateDiscoveryFixture();
        using var stager = new UpdateStager(trust.Verifier, trust.Clock, trust.Handler);
        _ = trust.Release();
        var manifest = ContractJson.Read<UpdateManifest>(trust.Handler.Assets[101], ManifestRules.ManifestLimit);
        var candidate = new UpdateCandidate(10, 103, GitHubUrls.ReleasePage(manifest.Tag), GitHubUrls.AssetApi(103),
            manifest, trust.Handler.Assets[101], trust.Handler.Assets[102], "offline fixture");
        if (pending) RetentionFixture.Write(Path.Combine(fixture.Install.Root, "update-journal.json"), ContractJson.Write(fixture.Install.Record));
        else for (var i = 0; i < UpdateStoragePolicy.MaximumStages; i++) fixture.Stage(stale: false);
        await Assert.ThrowsAnyAsync<Exception>(() => stager.PrepareManagedAsync(candidate, trust.Context, fixture.Install.Lease));
        Assert.Empty(trust.Handler.Requests);
        Assert.Equal(fixture.Install.OldApp, File.ReadAllBytes(fixture.Install.App));
    }

    [Fact]
    public async Task Preparation_capacity_failure_never_creates_an_additional_operation()
    {
        using var fixture = new PreparationFixture();
        using var client = fixture.Trust.Client();
        var root = Path.Combine(fixture.Install.Root, "update-operations");
        for (var i = 0; i < UpdateStoragePolicy.MaximumOperations; i++)
            ManagedInstallLease.CreatePrivateDirectory(Path.Combine(root, "operation-" + Guid.NewGuid().ToString("N")));
        var error = await Assert.ThrowsAsync<UpdatePreparationException>(() => fixture.Preparer(client).PrepareAsync(fixture.Install.OperationId, fixture.Context));
        Assert.Equal("preparation-validation-failed", error.Reason);
        Assert.Equal(UpdateStoragePolicy.MaximumOperations, Directory.GetDirectories(root).Length);
        Assert.Empty(fixture.Trust.Handler.Requests);
        fixture.AssertUntouched();
    }
}
