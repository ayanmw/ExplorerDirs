using System.Windows;

namespace ExplorerDirs.App;

public partial class VersionInfoWindow : Window
{
    internal VersionInfoWindow(BuildIdentity identity, ApplicationThemeController? themeController)
    {
        InitializeComponent();
        ApplicationThemeWindowBinding.Attach(this, themeController);
        VersionText.Text = $"版本 {identity.ProductVersion}";
        GitCommitText.Text = identity.GitCommit;
        CommitTimeText.Text = identity.GitCommitTimeUtc;
        SourceStateText.Text = identity.SourceTreeState;
        ConfigurationText.Text = identity.Configuration;
        FrameworkText.Text = identity.TargetFramework;
        RuntimeText.Text = string.IsNullOrEmpty(identity.RuntimeIdentifier) ? "未指定" : identity.RuntimeIdentifier;
        SelfContainedText.Text = identity.SelfContained;
        ConstantsText.Text = identity.DefineConstants;
        AotText.Text = identity.PublishAot;
        SingleFileText.Text = identity.PublishSingleFile;
        TrimmedText.Text = identity.PublishTrimmed;
        ReadyToRunText.Text = identity.PublishReadyToRun;
        BuildTagsText.Text = identity.BuildTags;
        InformationalVersionText.Text = identity.InformationalVersion;
    }
}
