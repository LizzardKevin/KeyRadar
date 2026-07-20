using System.IO.Compression;
using KeyRadar.Updater.Updates;

namespace KeyRadar.Updater.Tests.Updates;

public sealed class UpdateArchiveExtractorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"KeyRadar-ArchiveTest-{Guid.NewGuid():N}");

    [Fact]
    public void Extract_ExtractsValidatedApplicationArchive()
    {
        var archive = CreateArchive(
            ("KeyRadar.exe", "app"),
            ("KeyRadar.Updater.exe", "updater"),
            ("runtimes/win-x64/native/dependency.dll", "native"));
        var destination = Path.Combine(_root, "staging");

        var result = UpdateArchiveExtractor.Extract(archive, destination);

        Assert.True(result.IsValid);
        Assert.Equal("app", File.ReadAllText(Path.Combine(destination, "KeyRadar.exe")));
        Assert.Equal("native", File.ReadAllText(Path.Combine(destination, "runtimes", "win-x64", "native", "dependency.dll")));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("data/settings.json")]
    [InlineData("scripts/install.ps1")]
    public void Extract_RejectsUnsafeOrReservedEntry(string entryName)
    {
        var archive = CreateArchive(
            ("KeyRadar.exe", "app"),
            ("KeyRadar.Updater.exe", "updater"),
            (entryName, "bad"));

        var result = UpdateArchiveExtractor.Extract(archive, Path.Combine(_root, "staging"));

        Assert.False(result.IsValid);
        Assert.False(File.Exists(Path.Combine(_root, "outside.txt")));
    }

    [Fact]
    public void Extract_RejectsArchiveMissingRequiredExecutables()
    {
        var archive = CreateArchive(("README.md", "missing binaries"));

        var result = UpdateArchiveExtractor.Extract(archive, Path.Combine(_root, "staging"));

        Assert.False(result.IsValid);
        Assert.Equal(UpdateArchiveValidationError.MissingApplication, result.Error);
    }

    private string CreateArchive(params (string Name, string Content)[] entries)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, $"{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }

        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
