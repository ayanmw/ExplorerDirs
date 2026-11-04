using System.Reflection;

namespace ExplorerDirs.App;

internal sealed record BuildIdentity(
    string ProductVersion,
    string InformationalVersion,
    string GitCommit,
    string GitCommitTimeUtc,
    string SourceTreeState,
    string Configuration,
    string TargetFramework,
    string RuntimeIdentifier,
    string SelfContained,
    string DefineConstants,
    string PublishAot,
    string PublishSingleFile,
    string PublishTrimmed,
    string PublishReadyToRun,
    string BuildTags,
    string NativeBuildParameters)
{
    public string ShortCommit => GitCommit.Length >= 8 ? GitCommit[..8] : GitCommit;

    public static BuildIdentity Read(Assembly? assembly = null)
    {
        assembly ??= typeof(BuildIdentity).Assembly;
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value ?? string.Empty,
                StringComparer.Ordinal);
        string Get(string key) => metadata.GetValueOrDefault(key, "unknown");

        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        return new BuildIdentity(
            informationalVersion.Split('+', 2)[0],
            informationalVersion,
            Get("GitCommit"), Get("GitCommitTimeUtc"), Get("SourceTreeState"),
            Get("BuildConfiguration"), Get("BuildTargetFramework"), Get("BuildRuntimeIdentifier"),
            Get("BuildSelfContained"), Get("BuildDefineConstants"), Get("BuildPublishAot"),
            Get("BuildPublishSingleFile"), Get("BuildPublishTrimmed"), Get("BuildPublishReadyToRun"),
            Get("BuildTags"), Get("NativeBuildParameters"));
    }
}
