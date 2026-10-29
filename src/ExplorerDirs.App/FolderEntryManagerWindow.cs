using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace ExplorerDirs.App;

internal sealed class FolderEntryManagerWindow : Window
{
    private readonly FolderEntryManager _manager;
    private readonly DataGrid _states = new() { AutoGenerateColumns = false, CanUserAddRows = false, IsReadOnly = true };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly TextBlock _previewText = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly CheckBox _verified = new() { Content = "我已实际验证此发行版可以接收文件夹路径。", Margin = new Thickness(8), IsEnabled = false };
    private readonly Button _preview = new() { Content = "预览当前发行版" };
    private readonly Button _enable = new() { Content = "启用" , IsEnabled = false };
    private readonly Button _disable = new() { Content = "禁用" , IsEnabled = false };
    private FolderEntryPreview? _currentPreview;
    private bool _busy;

    public FolderEntryManagerWindow(ApplicationThemeController? themeController)
    {
        _manager = new FolderEntryManager(Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName!);
        ApplicationThemeWindowBinding.Attach(this, themeController);
        Title = "文件夹入口";
        Width = 700;
        Height = 440;
        MinWidth = 560;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _states.Columns.Add(new DataGridTextColumn { Header = "用户级 Shell 入口", Binding = new System.Windows.Data.Binding(nameof(FolderEntryState.Path)), Width = new DataGridLength(3, DataGridLengthUnitType.Star) });
        _states.Columns.Add(new DataGridTextColumn { Header = "状态", Binding = new System.Windows.Data.Binding(nameof(FolderEntryState.DisplayState)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _preview.Click += async (_, _) => await PreviewAsync();
        _verified.Checked += (_, _) => UpdateCommands();
        _verified.Unchecked += (_, _) => UpdateCommands();
        _enable.Click += async (_, _) => await EnableAsync();
        _disable.Click += async (_, _) => await DisableAsync();
        var refresh = new Button { Content = "刷新状态" };
        refresh.Click += async (_, _) => await RefreshAsync();
        var close = new Button { Content = "关闭", IsCancel = true };
        close.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
        foreach (var button in new[] { refresh, _preview, _enable, _disable, close })
        {
            button.Margin = new Thickness(0, 0, 8, 0);
            buttons.Children.Add(button);
        }
        var root = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(_status, Dock.Bottom);
        DockPanel.SetDock(_verified, Dock.Bottom);
        DockPanel.SetDock(_previewText, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(_status);
        root.Children.Add(_verified);
        root.Children.Add(_previewText);
        root.Children.Add(_states);
        Content = root;

        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_busy) return;
        if (!_manager.IsAvailable)
        {
            _states.ItemsSource = null;
            _status.Text = "当前是源码调试目录。该功能只在发布包中开放，避免调试版本注册到系统文件夹菜单。";
            _preview.IsEnabled = _enable.IsEnabled = _disable.IsEnabled = false;
            _verified.IsEnabled = false;
            return;
        }

        await RunActionAsync(async () =>
        {
            _states.ItemsSource = await _manager.ReadStatusAsync();
            _status.Text = "状态来自当前用户的文件夹和驱动器菜单。冲突项不会被覆盖；禁用时只移除仍由本应用所有的项目。";
            UpdateCommands();
        });
    }

    private async Task PreviewAsync()
    {
        await RunActionAsync(async () =>
        {
            _currentPreview = await _manager.PreviewAsync();
            var runningPath = Path.GetFullPath(Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName!);
            var isCurrentRelease = string.Equals(Path.GetFullPath(_currentPreview.Executable), runningPath, StringComparison.OrdinalIgnoreCase);
            _previewText.Text = $"目标：{_currentPreview.Executable}\nSHA-256：{_currentPreview.Sha256}\n接收文件夹路径能力：{(_currentPreview.DirectoryArgumentDeclared ? "已声明" : "未声明")}" +
                (isCurrentRelease ? "" : "\n当前运行的程序与预览目标不是同一发行版；不能从此窗口启用。");
            _verified.IsEnabled = _currentPreview.DirectoryArgumentDeclared && isCurrentRelease;
            _verified.IsChecked = false;
            _states.ItemsSource = _currentPreview.CurrentState;
            UpdateCommands();
        });
    }

    private async Task EnableAsync()
    {
        if (_currentPreview is null || !_verified.IsChecked.GetValueOrDefault()) return;
        var answer = MessageBox.Show(this,
            $"将为当前用户添加资源管理器文件夹/驱动器菜单入口，并启动本发行版：\n\n{_currentPreview.Executable}\n\nSHA-256：{_currentPreview.Sha256}\n\n继续启用？",
            "启用文件夹入口", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        await RunActionAsync(async () =>
        {
            _states.ItemsSource = await _manager.EnableAsync(_currentPreview.Executable, _currentPreview.Sha256);
            _status.Text = "文件夹入口已启用。";
            _verified.IsChecked = false;
            UpdateCommands();
        });
    }

    private async Task DisableAsync()
    {
        var answer = MessageBox.Show(this, "仅移除仍匹配本应用所有权标记的文件夹入口。继续禁用？",
            "禁用文件夹入口", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        await RunActionAsync(async () =>
        {
            _states.ItemsSource = await _manager.DisableAsync();
            _status.Text = "文件夹入口已禁用。";
            UpdateCommands();
        });
    }

    private async Task RunActionAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        UpdateCommands();
        try { await action(); }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "文件夹入口操作失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _busy = false; UpdateCommands(); }
    }

    private void UpdateCommands()
    {
        _preview.IsEnabled = !_busy && _manager.IsAvailable;
        _enable.IsEnabled = !_busy && _currentPreview?.DirectoryArgumentDeclared == true &&
            _verified.IsEnabled && _verified.IsChecked == true &&
            (_states.ItemsSource as IEnumerable<FolderEntryState>)?.Any(state => state.State == "conflict") != true;
        _disable.IsEnabled = !_busy && (_states.ItemsSource as IEnumerable<FolderEntryState>)?.Any(state => state.State == "owned") == true;
    }
}
