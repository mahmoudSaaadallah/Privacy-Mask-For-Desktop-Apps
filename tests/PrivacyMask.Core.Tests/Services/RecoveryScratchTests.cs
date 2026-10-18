using System.IO;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class RecoveryScratchTests
{
    [Theory]
    [InlineData("app")]
    [InlineData("primary")]
    [InlineData("secondary")]
    public void Repeated_locked_recovery_reuses_one_verified_scratch_without_consuming_the_backup(string label)
    {
        using var f = new RecoveryFixture();
        f.ReplaceFiles();
        var target = label switch { "primary" => f.Primary, "secondary" => f.Secondary, _ => f.App };
        using (var locked = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
            for (var attempt = 0; attempt < 5; attempt++)
            {
                Assert.Equal(RecoveryOutcome.RepairRequired, new RecoveryRestorer(f.Lease).Recover().Outcome);
                Assert.Single(Directory.GetFiles(f.Operation, "restore-*.tmp"));
                Assert.True(File.Exists(Path.Combine(f.Operation, "original-app.exe")));
            }
        Assert.Equal(RecoveryOutcome.Recovered, new RecoveryRestorer(f.Lease).Recover().Outcome);
        Assert.Empty(Directory.GetFiles(f.Operation, "restore-*.tmp"));
        f.AssertOriginal();
    }

    [Fact]
    public void Corrupt_scratch_is_preserved_and_never_replaced_or_used_to_restore()
    {
        using var f = new RecoveryFixture();
        f.ReplaceFiles();
        var path = Path.Combine(f.Operation, "restore-app.tmp");
        RetentionFixture.Write(path, [99]);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.Equal(RecoveryOutcome.RepairRequired, new RecoveryRestorer(f.Lease).Recover().Outcome);
            Assert.Equal(new byte[] { 99 }, File.ReadAllBytes(path));
            Assert.Equal(f.NewApp, File.ReadAllBytes(f.App));
            Assert.Single(Directory.GetFiles(f.Operation, "restore-*.tmp"));
        }
    }
}
