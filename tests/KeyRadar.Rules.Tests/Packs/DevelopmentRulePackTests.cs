using System.Diagnostics;
using KeyRadar.Conflicts;
using KeyRadar.Hotkeys;
using KeyRadar.Rules.Packs;

namespace KeyRadar.Rules.Tests.Packs;

public sealed class DevelopmentRulePackTests
{
    [Fact]
    public void ResolveDotNetHost_uses_dotnet_host_path_when_it_exists()
    {
        const string repositoryRoot = @"C:\agent\_work\KeyRadar";
        const string dotnetHostPath = @"C:\hostedtoolcache\dotnet\dotnet.exe";

        var host = ResolveDotNetHost(
            repositoryRoot,
            dotnetHostPath,
            path => path == dotnetHostPath);

        Assert.Equal(dotnetHostPath, host);
    }

    [Fact]
    public void ResolveDotNetHost_uses_repository_tools_dotnet_when_no_valid_dotnet_host_path_exists()
    {
        const string repositoryRoot = @"C:\agent\_work\KeyRadar";
        var repositoryDotNet = Path.Combine(repositoryRoot, ".tools", "dotnet", "dotnet.exe");

        var host = ResolveDotNetHost(
            repositoryRoot,
            @"C:\missing\dotnet.exe",
            path => path == repositoryDotNet);

        Assert.Equal(repositoryDotNet, host);
    }

    [Fact]
    public void ResolveDotNetHost_uses_path_dotnet_when_no_valid_host_or_repository_tools_dotnet_exists()
    {
        var host = ResolveDotNetHost(
            @"C:\agent\_work\KeyRadar",
            @"C:\missing\dotnet.exe",
            _ => false);

        Assert.Equal("dotnet", host);
    }

    [Fact]
    public void ResolveDotNetHost_uses_actual_test_host_when_dotnet_host_path_is_available()
    {
        var dotnetHostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");

        if (!string.IsNullOrWhiteSpace(dotnetHostPath) && File.Exists(dotnetHostPath))
        {
            Assert.Equal(
                dotnetHostPath,
                ResolveDotNetHost(FindRepositoryRoot(), dotnetHostPath, File.Exists));
        }
    }

    [Fact]
    public void Development_rule_pack_is_copied_only_to_Debug_app_output()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appProject = Path.Combine(repositoryRoot, "src", "KeyRadar.App", "KeyRadar.App.csproj");
        var debugPack = Path.Combine(
            repositoryRoot,
            "src",
            "KeyRadar.App",
            "bin",
            "Debug",
            "net10.0-windows10.0.19041.0",
            "win-x64",
            "KeyRadar-Development-Rules.krpack");
        var releasePack = Path.Combine(
            repositoryRoot,
            "src",
            "KeyRadar.App",
            "bin",
            "Release",
            "net10.0-windows10.0.19041.0",
            "win-x64",
            "KeyRadar-Development-Rules.krpack");

        DeleteFileIfExists(debugPack);
        DeleteFileIfExists(releasePack);

        RunDotNet(repositoryRoot, "build", appProject, "-c", "Debug", "--no-restore", "--nologo", "-v:minimal");
        Assert.True(File.Exists(debugPack), "Debug output must contain the unsigned development rule pack.");

        RunDotNet(repositoryRoot, "build", appProject, "-c", "Release", "--no-restore", "--nologo", "-v:minimal");
        Assert.False(File.Exists(releasePack), "Release output must not contain a development rule pack.");
    }

    [Fact]
    public void Debug_rule_pack_is_safely_readable_and_preserves_system_and_nvidia_attribution()
    {
        var repositoryRoot = FindRepositoryRoot();
        var scriptPath = Path.Combine(repositoryRoot, "eng", "Build-DebugRulePack.ps1");
        var outputPath = Path.Combine(Path.GetTempPath(), $"KeyRadar-DevelopmentRules-{Guid.NewGuid():N}.krpack");

        try
        {
            Assert.True(File.Exists(scriptPath), "The Debug rule-pack build script must exist.");
            RunPowerShell(
                scriptPath,
                Path.Combine(repositoryRoot, "rules"),
                outputPath);

            using var package = File.OpenRead(outputPath);
            var result = RulePackReader.ReadLocal(package);

            Assert.True(result.IsSuccess, result.Message);
            Assert.Equal("keyradar.local", result.Pack!.PackId);
            Assert.Equal("1.0.0", result.Pack.Version);

            var windows = Assert.Single(result.Pack.Variants, variant =>
                variant.ApplicationId == "windows-system");
            var closeWindow = Assert.Single(windows.Hotkeys, hotkey =>
                hotkey.Gesture == HotkeyGesture.Parse("Alt+F4"));
            Assert.Equal(OwnershipConfidence.SystemKnown, closeWindow.Confidence);

            var nvidia = Assert.Single(result.Pack.Variants, variant =>
                variant.ApplicationId == "nvidia-app");
            var overlay = Assert.Single(nvidia.Hotkeys, hotkey =>
                hotkey.Gesture == HotkeyGesture.Parse("Alt+Z"));
            Assert.Equal(OwnershipConfidence.OfficialDefault, overlay.Confidence);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    private static void RunPowerShell(string scriptPath, string rulesDirectory, string outputPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-RulesDirectory");
        startInfo.ArgumentList.Add(rulesDirectory);
        startInfo.ArgumentList.Add("-OutputPath");
        startInfo.ArgumentList.Add(outputPath);
        startInfo.ArgumentList.Add("-Version");
        startInfo.ArgumentList.Add("1.0.0");

        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"{standardOutput}\n{standardError}");
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void RunDotNet(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveDotNetHost(
                workingDirectory,
                Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"),
                File.Exists),
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"{standardOutput}\n{standardError}");
    }

    private static string ResolveDotNetHost(
        string repositoryRoot,
        string? dotnetHostPath,
        Func<string, bool> fileExists)
    {
        if (!string.IsNullOrWhiteSpace(dotnetHostPath) && fileExists(dotnetHostPath))
        {
            return dotnetHostPath;
        }

        var repositoryDotNet = Path.Combine(repositoryRoot, ".tools", "dotnet", "dotnet.exe");
        return fileExists(repositoryDotNet) ? repositoryDotNet : "dotnet";
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KeyRadar.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("KeyRadar repository root was not found.");
    }
}
