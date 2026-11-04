using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ExplorerDirs.SettingsSmoke;

internal static class BuildIdentitySmoke
{
    internal static void Run(string applicationAssemblyPath)
    {
        var assembly = Assembly.LoadFrom(Path.GetFullPath(applicationAssemblyPath));
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value ?? string.Empty,
                StringComparer.Ordinal);
        var commit = RunGit("rev-parse", "HEAD");
        var committedTime = DateTimeOffset.Parse(RunGit("log", "-1", "--format=%cI"))
            .ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var informationVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var mainWindow = File.ReadAllText("src/ExplorerDirs.App/MainWindow.xaml");
        var mainWindowCode = File.ReadAllText("src/ExplorerDirs.App/MainWindow.xaml.cs");
        var versionWindowPath = "src/ExplorerDirs.App/VersionInfoWindow.xaml";
        Require(File.Exists(versionWindowPath), "the themed version information window exists");
        var versionWindow = File.ReadAllText(versionWindowPath);
        var project = File.ReadAllText("src/ExplorerDirs.App/ExplorerDirs.App.csproj");
        var publishScript = File.ReadAllText("scripts/publish.ps1");

        Require(metadata.GetValueOrDefault("GitCommit") == commit, "binary embeds its full Git commit");
        Require(metadata.GetValueOrDefault("GitCommitTimeUtc") == committedTime,
            "binary embeds the Git committer time normalized to UTC");
        Require(metadata.GetValueOrDefault("SourceTreeState") is "clean" or "dirty",
            "binary identifies whether its source tree was clean");
        Require(metadata.GetValueOrDefault("BuildConfiguration") == "Release",
            "binary records the effective build configuration");
        Require(metadata.GetValueOrDefault("BuildTargetFramework") == "net10.0-windows10.0.19041.0",
            "binary records the target framework");
        Require(metadata.ContainsKey("BuildRuntimeIdentifier") && metadata.ContainsKey("BuildSelfContained") &&
                metadata.ContainsKey("BuildDefineConstants") && metadata.ContainsKey("BuildTags"),
            "binary records the runtime, self-contained, compiler-constant and build-tag parameters");
        Require(metadata.GetValueOrDefault("NativeBuildParameters") is { } nativeParameters &&
                nativeParameters.Contains("Release: /O2 /DNDEBUG /MD", StringComparison.Ordinal) &&
                nativeParameters.Contains("/Brepro", StringComparison.Ordinal),
            "binary records the optimized native Shell build parameters");
        Require(informationVersion.Contains("+" + commit, StringComparison.Ordinal),
            "informational version identifies the same source commit");
        Require(!metadata.Keys.Any(key => key.Contains("BuildTime", StringComparison.Ordinal)),
            "binary does not embed wall-clock build time");
        Require(mainWindow.Contains("Header=\"关于(_A)\"", StringComparison.Ordinal) &&
                mainWindow.Contains("Header=\"版本信息…\"", StringComparison.Ordinal) &&
                mainWindowCode.Contains("BuildIdentity", StringComparison.Ordinal) &&
                mainWindowCode.Contains("_buildIdentity.ShortCommit", StringComparison.Ordinal),
            "application menu opens the embedded version information");
        Require(versionWindow.Contains("ExplorerDirs.WindowBackgroundBrush", StringComparison.Ordinal) &&
                versionWindow.Contains("ExplorerDirs.WindowTextBrush", StringComparison.Ordinal),
            "version information window follows the application theme");
        Require(project.Contains("AssemblyMetadata", StringComparison.Ordinal) &&
                publishScript.Contains("GitCommitTimeUtc", StringComparison.Ordinal),
            "MSBuild and release packaging carry the same metadata schema");
        Console.WriteLine("Build identity smoke: PASS");
    }

    private static string RunGit(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(Environment.CurrentDirectory);
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start git.");
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("Git metadata lookup failed.");
        return output;
    }

    private static void Require(bool condition, string behavior)
    {
        if (!condition) throw new InvalidOperationException("Build identity smoke failed: " + behavior);
    }
}
