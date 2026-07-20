using KeyRadar.Updater.Updates;

namespace KeyRadar.Updater.Tests.Updates;

public sealed class FileUpdateTransactionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"KeyRadar.Updater.Tests-{Guid.NewGuid():N}");

    [Fact]
    public void Apply_replaces_application_files_but_preserves_data()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        var target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        var backup = Path.Combine(_root, "backup");
        Directory.CreateDirectory(Path.Combine(source, "data"));
        Directory.CreateDirectory(Path.Combine(target, "data"));
        File.WriteAllText(Path.Combine(source, "KeyRadar.exe"), "new");
        File.WriteAllText(Path.Combine(source, "data", "settings.json"), "source-must-not-win");
        File.WriteAllText(Path.Combine(target, "KeyRadar.exe"), "old");
        File.WriteAllText(Path.Combine(target, "data", "settings.json"), "user-data");

        var result = FileUpdateTransaction.Apply(source, target, backup);

        Assert.True(result.Succeeded);
        Assert.Equal("new", File.ReadAllText(Path.Combine(target, "KeyRadar.exe")));
        Assert.Equal("user-data", File.ReadAllText(Path.Combine(target, "data", "settings.json")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(backup, "KeyRadar.exe")));
    }

    [Fact]
    public void Copy_failure_restores_every_file_already_replaced()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        var target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        var backup = Path.Combine(_root, "backup");
        File.WriteAllText(Path.Combine(source, "a.txt"), "new-a");
        File.WriteAllText(Path.Combine(source, "b.txt"), "new-b");
        File.WriteAllText(Path.Combine(target, "a.txt"), "old-a");
        File.WriteAllText(Path.Combine(target, "b.txt"), "old-b");
        using var locked = new FileStream(Path.Combine(target, "b.txt"), FileMode.Open, FileAccess.Read, FileShare.None);

        var result = FileUpdateTransaction.Apply(source, target, backup);

        Assert.False(result.Succeeded);
        Assert.Equal("old-a", File.ReadAllText(Path.Combine(target, "a.txt")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
