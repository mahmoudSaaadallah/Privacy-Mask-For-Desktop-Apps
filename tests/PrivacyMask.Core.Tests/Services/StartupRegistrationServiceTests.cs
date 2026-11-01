using PrivacyMask.Windows.Services;

namespace PrivacyMask.Core.Tests.Services;

public sealed class StartupRegistrationServiceTests
{
    private const string Path = @"C:\Users\مستخدم\Local App\PrivacyMask.Launcher.exe";
    private const string Command = "\"" + Path + "\"";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Repeated_startup_with_unchanged_preferences_performs_no_writes(bool minimized)
    {
        var store = new MemoryStore { Value = Command + (minimized ? " --minimized" : "") };
        var service = new StartupRegistrationService(Path, store);

        for (var start = 0; start < 25; start++)
        {
            Assert.Equal(new StartupRegistrationState(true, minimized), service.GetState());
            service.SetEnabled(true, minimized);
        }

        Assert.Equal(0, store.Writes);
        Assert.Equal(0, store.Deletes);
    }

    [Fact]
    public void Disabled_missing_registration_is_read_only_including_repeated_disables()
    {
        var store = new MemoryStore();
        var service = new StartupRegistrationService(Path, store);
        Assert.Equal(StartupRegistrationState.Disabled, service.GetState());
        service.SetEnabled(false, true);
        service.SetEnabled(false, false);
        Assert.Equal(0, store.Writes);
        Assert.Equal(0, store.Deletes);
    }

    [Fact]
    public void Enabling_changing_minimized_and_disabling_each_mutate_once()
    {
        var store = new MemoryStore();
        var service = new StartupRegistrationService(Path, store);
        service.SetEnabled(true, true);
        Assert.Equal(Command + " --minimized", store.Value);
        service.SetEnabled(true, true);
        Assert.Equal(1, store.Writes);
        service.SetEnabled(true, false);
        Assert.Equal(Command, store.Value);
        service.SetEnabled(true, false);
        Assert.Equal(2, store.Writes);
        service.SetEnabled(false, false);
        service.SetEnabled(false, true);
        Assert.Null(store.Value);
        Assert.Equal(1, store.Deletes);
    }

    [Theory]
    [InlineData("cmd.exe /c ")]
    [InlineData("powershell.exe -File ")]
    public void Command_wrappers_containing_the_path_are_not_our_registration(string prefix)
    {
        var store = new MemoryStore { Value = prefix + Command + " --minimized" };
        Assert.Equal(StartupRegistrationState.Disabled, new StartupRegistrationService(Path, store).GetState());
        Assert.Equal(0, store.Writes);
    }

    [Theory]
    [InlineData(" --minimized --unknown")]
    [InlineData(" --unknown")]
    [InlineData(".other.exe")]
    public void Trailing_arguments_or_path_substrings_do_not_enable_startup(string suffix)
    {
        var store = new MemoryStore { Value = Command + suffix };
        Assert.Equal(StartupRegistrationState.Disabled, new StartupRegistrationService(Path, store).GetState());
    }

    [Fact]
    public void Read_accepts_case_and_surrounding_whitespace_without_mutation()
    {
        var store = new MemoryStore { Value = "  " + Command.ToUpperInvariant() + " --MINIMIZED  " };
        Assert.Equal(new StartupRegistrationState(true, true), new StartupRegistrationService(Path, store).GetState());
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public void Non_string_values_are_disabled_and_are_repaired_only_on_explicit_enable()
    {
        var store = new MemoryStore { Value = 42 };
        var service = new StartupRegistrationService(Path, store);
        Assert.Equal(StartupRegistrationState.Disabled, service.GetState());
        Assert.Equal(0, store.Writes);
        service.SetEnabled(true, false);
        Assert.Equal(Command, store.Value);
        Assert.Equal(1, store.Writes);
    }

    [Fact]
    public void Access_failures_propagate_without_attempting_a_write()
    {
        var store = new MemoryStore { FailRead = true };
        var service = new StartupRegistrationService(Path, store);
        Assert.Throws<UnauthorizedAccessException>(() => service.GetState());
        Assert.Throws<UnauthorizedAccessException>(() => service.SetEnabled(true, true));
        Assert.Equal(0, store.Writes);
        Assert.Equal(0, store.Deletes);
    }

    private sealed class MemoryStore : IStartupRegistrationStore
    {
        internal object? Value;
        internal int Writes;
        internal int Deletes;
        internal bool FailRead;
        public object? Read() => FailRead ? throw new UnauthorizedAccessException("Fixture read denied.") : Value;
        public void Write(string command) { Writes++; Value = command; }
        public void Delete() { Deletes++; Value = null; }
    }
}
