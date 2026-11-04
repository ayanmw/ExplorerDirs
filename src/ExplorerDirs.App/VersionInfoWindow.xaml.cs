using System.Runtime.InteropServices;
using System.Windows;

namespace ExplorerDirs.App;

public partial class VersionInfoWindow : Window
{
    private readonly string _feedbackVersion;

    internal VersionInfoWindow(BuildIdentity identity, ApplicationThemeController? themeController)
    {
        InitializeComponent();
        _feedbackVersion = $"{identity.ProductVersion} · {identity.ShortCommit}";
        CopyVersionButton.ToolTip = _feedbackVersion;
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
        NativeBuildText.Text = identity.NativeBuildParameters;
        BuildTagsText.Text = identity.BuildTags;
        InformationalVersionText.Text = identity.InformationalVersion;
    }

    private void CopyVersion_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_feedbackVersion, TextDataFormat.UnicodeText);
            CopyStatusText.Text = "已复制版本信息。";
        }
        catch (Exception error) when (error is ExternalException or InvalidOperationException)
        {
            CopyStatusText.Text = $"复制失败，请重试：{error.Message}";
        }
    }
}
