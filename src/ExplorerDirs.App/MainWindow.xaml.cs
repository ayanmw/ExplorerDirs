using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ExplorerDirs.App;

public partial class MainWindow : Window
{
    private const int WmSettingChange = 0x001A;
    private readonly PaneControl[] _panes = new PaneControl[4];
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly SessionStore _sessionStore = new();
    private readonly EnvironmentRefresh _environmentRefresh = new();
    private int _layout = 4;
    private bool _initializing = true;

    public MainWindow()
    {
        InitializeComponent();
        var state = _sessionStore.Load();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        for (var index = 0; index < _panes.Length; index++)
        {
            var paths = state?.Panes.ElementAtOrDefault(index);
            var pane = new PaneControl(paths is { Count: > 0 } ? paths : [home]);
            _panes[index] = pane;
            pane.StateChanged += (_, _) => ScheduleSave();
            pane.GotKeyboardFocus += (_, _) => ActivePane = pane;
        }

        ActivePane = _panes[0];
        _layout = state?.Layout is 1 or 2 or 4 ? state.Layout : 4;
        LayoutSelector.SelectedIndex = _layout == 1 ? 0 : _layout == 2 ? 1 : 2;
        BuildLayout();
        _initializing = false;
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveSession();
        };
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowHook);
        Closing += (_, _) => SaveSession();
    }

    private PaneControl ActivePane { get; set; }

    private void LayoutSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || LayoutSelector.SelectedItem is not ComboBoxItem item ||
            !int.TryParse(item.Tag?.ToString(), out var layout))
            return;

        _layout = layout;
        BuildLayout();
        ScheduleSave();
    }

    private void BuildLayout()
    {
        PaneGrid.Children.Clear();
        PaneGrid.RowDefinitions.Clear();
        PaneGrid.ColumnDefinitions.Clear();
        var columns = _layout == 1 ? 1 : 2;
        var rows = _layout == 4 ? 2 : 1;
        for (var col = 0; col < columns; col++)
            PaneGrid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var row = 0; row < rows; row++)
            PaneGrid.RowDefinitions.Add(new RowDefinition());
        for (var index = 0; index < _layout; index++)
        {
            var pane = _panes[index];
            Grid.SetColumn(pane, index % columns);
            Grid.SetRow(pane, index / columns);
            PaneGrid.Children.Add(pane);
        }

        if (!PaneGrid.Children.Contains(ActivePane))
            ActivePane = _panes[0];
    }

    private void RefreshEnvironment_Click(object sender, RoutedEventArgs e) => RefreshEnvironment();

    private void RefreshEnvironment()
    {
        try
        {
            var count = _environmentRefresh.Reload();
            EnvironmentStatus.Text = $"已刷新 {count} 个持久化变量 · {DateTime.Now:T}";
        }
        catch (Exception ex)
        {
            EnvironmentStatus.Text = $"环境变量刷新失败：{ex.Message}";
        }
    }

    private IntPtr WindowHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmSettingChange && lParam != IntPtr.Zero &&
            string.Equals(System.Runtime.InteropServices.Marshal.PtrToStringUni(lParam), "Environment", StringComparison.OrdinalIgnoreCase))
            Dispatcher.BeginInvoke(RefreshEnvironment);
        return IntPtr.Zero;
    }

    private void OpenTerminal_Click(object sender, RoutedEventArgs e)
    {
        var path = ActivePane.CurrentPath;
        if (!Directory.Exists(path))
        {
            MessageBox.Show(this, "当前目录不可用。", "Explorer Dirs", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe")
            {
                WorkingDirectory = path,
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "无法打开命令行", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ScheduleSave()
    {
        if (!_initializing)
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }
    }

    private void SaveSession()
    {
        try
        {
            _sessionStore.Save(new SessionState(_layout, _panes.Select(p => p.Paths.ToList()).ToList()));
        }
        catch (Exception ex)
        {
            EnvironmentStatus.Text = $"会话保存失败：{ex.Message}";
        }
    }
}
