using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using PrivacyMask.ReleaseTool;
using PrivacyMask.Updates;

namespace PrivacyMask.Core.Tests.Services;

public sealed class ReleaseArchiveTests
{
    [Theory]
    [InlineData("../PrivacyMask.App.exe")]
    [InlineData("C:/PrivacyMask.App.exe")]
    [InlineData("PrivacyMask.App.exe:stream")]
    [InlineData("privacymask.app.exe")]
    [InlineData("settings.v1.json")]
    [InlineData("PrivacyMask.Launcher.exe")]
    [InlineData("folder/PrivacyMask.App.exe")]
    public void RejectsNonAllowlistedArchiveEntries(string path)
    {
        using var memory = Zip(path);
        using var zip = new ZipArchive(memory);
        Assert.Throws<ReleaseValidationException>(() => UpdatePackages.ValidateEntry(zip, Expected));
    }

    [Theory]
    [InlineData(0xa000, 0)] // Unix symlink
    [InlineData(0x4000, 0)] // Unix directory
    [InlineData(0, 0x400)] // Windows reparse point
    [InlineData(0, 0x10)] // DOS directory
    public void RejectsNonRegularEntries(int unixType, int windowsFlags)
    {
        using var memory = Zip("PrivacyMask.App.exe", (unixType << 16) | windowsFlags);
        using var zip = new ZipArchive(memory);
        Assert.Throws<ReleaseValidationException>(() => UpdatePackages.ValidateEntry(zip, Expected));
    }

    [Fact]
    public void AcceptsOneRegularFileAndRejectsExtrasAndWrongSize()
    {
        using var memory = Zip("PrivacyMask.App.exe");
        using var zip = new ZipArchive(memory);
        UpdatePackages.ValidateEntry(zip, Expected);
        Assert.Throws<ReleaseValidationException>(() => UpdatePackages.ValidateEntry(zip, Expected with { Size = 5 }));
        using var extraMemory = Zip("PrivacyMask.App.exe", extra: true);
        using var extras = new ZipArchive(extraMemory);
        Assert.Throws<ReleaseValidationException>(() => UpdatePackages.ValidateEntry(extras, Expected));
    }

    [Fact]
    public void ValidatesDirectoryBoundsBeforeZipAllocation()
    {
        using var valid = Zip("PrivacyMask.App.exe");
        UpdatePayload.ValidateArchiveLayout(valid);
        Assert.Equal(0, valid.Position);
        using var extra = Zip("PrivacyMask.App.exe", extra: true);
        Assert.Throws<ReleaseValidationException>(() => UpdatePayload.ValidateArchiveLayout(extra));
    }

    [Theory]
    [InlineData(4, 1)] // multiple disks
    [InlineData(6, 1)]
    [InlineData(8, 255)] // too many entries / ZIP64 sentinel
    [InlineData(10, 255)]
    [InlineData(12, 255)] // central directory size mismatch
    [InlineData(16, 255)] // central directory offset mismatch
    [InlineData(20, 1)] // comments not supported
    [InlineData(0, 0)] // missing end signature
    public void RejectsUnsupportedArchiveDirectory(int offset, byte value)
    {
        using var memory = Zip("PrivacyMask.App.exe");
        var bytes = memory.ToArray();
        bytes[bytes.Length - 22 + offset] = value;
        using var malformed = new MemoryStream(bytes);
        Assert.Throws<ReleaseValidationException>(() => UpdatePayload.ValidateArchiveLayout(malformed));
    }

    [Fact]
    public void StreamCopyEnforcesExactHashAndSize()
    {
        using var source = new MemoryStream(Payload);
        using var target = new MemoryStream();
        UpdatePackages.CopyBounded(source, target, Expected);
        Assert.Equal(Payload, target.ToArray());
        using var oversized = new MemoryStream([1, 2, 3, 4, 5]);
        Assert.Throws<ReleaseValidationException>(() => UpdatePackages.CopyBounded(oversized, Stream.Null, Expected));
        using var shortInput = new MemoryStream([1, 2]);
        Assert.Throws<ReleaseValidationException>(() => UpdatePackages.CopyBounded(shortInput, Stream.Null, Expected));
        using var altered = new MemoryStream([5, 6, 7, 8]);
        Assert.Throws<ReleaseValidationException>(() => UpdatePackages.CopyBounded(altered, Stream.Null, Expected));
    }

    [Fact]
    public void CliRejectsUnknownOptionsWithoutEchoingSensitiveArguments()
    {
        var previous = Console.Error;
        using var output = new StringWriter();
        try
        {
            Console.SetError(output);
            Assert.Equal(1, Program.Main(["pack", "--private-key", "secret-must-not-be-echoed"]));
            Assert.DoesNotContain("secret-must-not-be-echoed", output.ToString());
        }
        finally { Console.SetError(previous); }
    }

    private static readonly byte[] Payload = [1, 2, 3, 4];
    private static PayloadFile Expected => new("PrivacyMask.App.exe", Payload.Length,
        Convert.ToHexStringLower(SHA256.HashData(Payload)));

    private static MemoryStream Zip(string path, int attributes = 0, bool extra = false)
    {
        var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry(path);
            entry.ExternalAttributes = attributes;
            using (var stream = entry.Open()) stream.Write(Payload);
            if (extra) zip.CreateEntry(path);
        }
        memory.Position = 0;
        return memory;
    }
}
