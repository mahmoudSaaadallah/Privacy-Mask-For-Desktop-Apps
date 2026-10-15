using System.IO;
using PrivacyMask.App.Services;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.Core.Tests.Services;

/// <summary>Real JSON persistence with the production queue, not a live shell/registry/capture fixture.</summary>
public sealed class ShellSettingsDrainTests
{
    [Fact]
    public async Task QueuedMutations_CloneLatestSavedBaselineAndPreserveOtherPreferences()
    {
        var directory = Directory.CreateTempSubdirectory("PrivacyMask-shell-settings-");
        try
        {
            var factory = new DefaultSettingsFactory();
            var store = new JsonSettingsStore(factory, Path.Combine(directory.FullName, "settings.v1.json"));
            var saved = factory.Create();
            saved.OnboardingCompleted = true;
            saved.LaunchAtLogin = false;
            saved.StartMinimized = false;
            saved.PanicPin = PanicPinService.Create("1357");
            var custom = CustomAppProfileFactory.Create("Private notes", "SyntheticNotes", executablePath: @"C:\Synthetic\Notes.exe");
            saved.AppProfiles.Add(custom);
            await store.SaveAsync(saved);
            var originalPin = PanicPinService.Clone(saved.PanicPin)!;
            var queue = new ShellOperationQueue();
            var writeGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var first = queue.TryRunAsync(async () =>
            {
                var next = AppSettingsCloner.Clone(saved);
                next.AppProfiles[0].MaskIntensity = .4;
                await writeGate.Task;
                await store.SaveAsync(next);
                saved = next;
            });
            var second = queue.TryRunAsync(async () =>
            {
                var next = AppSettingsCloner.Clone(saved);
                next.AppProfiles.Single(profile => profile.ProfileId == custom.ProfileId).HoverRevealWidthPixels = 800;
                await store.SaveAsync(next);
                saved = next;
            });
            var third = queue.TryRunAsync(async () =>
            {
                var next = AppSettingsCloner.Clone(saved);
                next.GlobalHotkeys[0].Enabled = false;
                await store.SaveAsync(next);
                saved = next;
            });
            var drain = queue.StopAsync();
            Assert.False(drain.IsCompleted);
            writeGate.SetResult();
            await drain.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.All(await Task.WhenAll(first, second, third), Assert.True);
            var reloaded = await store.LoadAsync();
            Assert.Equal(.4, reloaded.AppProfiles[0].MaskIntensity);
            var reloadedCustom = Assert.Single(reloaded.AppProfiles, profile => profile.ProfileId == custom.ProfileId);
            Assert.Equal(800, reloadedCustom.HoverRevealWidthPixels);
            Assert.Equal(custom.ExecutablePath, reloadedCustom.ExecutablePath);
            Assert.Equal(custom.HoverRevealHeightPixels, reloadedCustom.HoverRevealHeightPixels);
            Assert.False(reloaded.GlobalHotkeys[0].Enabled);
            Assert.False(reloaded.LaunchAtLogin);
            Assert.False(reloaded.StartMinimized);
            Assert.True(reloaded.OnboardingCompleted);
            Assert.Equal(originalPin.Salt, reloaded.PanicPin!.Salt);
            Assert.Equal(originalPin.Hash, reloaded.PanicPin.Hash);
            Assert.Equal(originalPin.Iterations, reloaded.PanicPin.Iterations);
            Assert.True(PanicPinService.Verify("1357", reloaded.PanicPin));
            Assert.Empty(Directory.EnumerateFiles(directory.FullName, "*.tmp"));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task RealWriteFailure_IsObservedBeforeDrainWithoutReplacingSavedFile()
    {
        var directory = Directory.CreateTempSubdirectory("PrivacyMask-shell-settings-");
        try
        {
            var factory = new DefaultSettingsFactory();
            var path = Path.Combine(directory.FullName, "settings.v1.json");
            var store = new JsonSettingsStore(factory, path);
            var saved = factory.Create();
            await store.SaveAsync(saved);
            var original = await File.ReadAllTextAsync(path);
            var queue = new ShellOperationQueue();
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var failed = queue.TryRunAsync(async () =>
                {
                    var next = AppSettingsCloner.Clone(saved);
                    next.StartMinimized = !next.StartMinimized;
                    await store.SaveAsync(next);
                    saved = next;
                });
                await Assert.ThrowsAsync<IOException>(() => failed);
                await queue.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(saved.StartMinimized);
            }
            Assert.Equal(original, await File.ReadAllTextAsync(path));
            Assert.Empty(Directory.EnumerateFiles(directory.FullName, "*.tmp"));
        }
        finally { directory.Delete(recursive: true); }
    }
}
