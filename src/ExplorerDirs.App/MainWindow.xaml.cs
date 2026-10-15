using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ExplorerDirs.App;

public partial class MainWindow : Window
{
    private const int WmSettingChange = 0x001A;
    private readonly PaneControl[] _panes = new PaneControl[4];
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly SessionStore _sessionStore = new();
    private readonly EnvironmentRefresh _environmentRefresh = new();
    private readonly Dictionary<int, Button> _layoutButtons = [];
    private readonly Dictionary<int, MenuItem> _layoutMenuItems = [];
    private int _layout = 4;
    private bool _initializing = true;

    public MainWindow()
    {
        InitializeComponent();
#if DEBUG
        Title = "Explorer Dirs · 开发调试";
#else
        Title = $"Explorer Dirs · Preview {GetType().Assembly.GetName().Version?.ToString(3)}";
#endif
        var state = _sessionStore.Load();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        for (var index = 0; index < _panes.Length; index++)
        {
            var paths = state?.Panes.ElementAtOrDefault(index);
            var pane = new PaneControl(paths is { Count: > 0 } ? paths : [home]);
            _panes[index] = pane;
            pane.StateChanged += (_, _) => ScheduleSave();
            pane.GotKeyboardFocus += (_, _) => ActivePane = pane;
            pane.Activated += (_, _) => ActivePane = pane;
            pane.LayoutRequested += (_, layoutId) => SetLayout(layoutId);
        }

        ActivePane = _panes[0];
        _layout = LayoutPreset.Find(state?.Layout ?? 4).Id;
        BuildLayoutControls();
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

    private void BuildLayoutControls()
    {
        foreach (var preset in LayoutPreset.All)
        {
            var menuItem = new MenuItem { Header = preset.Name, IsCheckable = true };
            menuItem.Click += (_, _) => SetLayout(preset.Id);
            LayoutMenu.Items.Add(menuItem);
            _layoutMenuItems[preset.Id] = menuItem;

            var button = new Button
            {
                Content = CreateLayoutIcon(preset),
                ToolTip = preset.Name,
                Width = 34,
                Height = 28,
                Margin = new Thickness(1, 0, 1, 0)
            };
            button.Click += (_, _) => SetLayout(preset.Id);
            LayoutButtons.Children.Add(button);
            _layoutButtons[preset.Id] = button;
        }
    }

    private static Grid CreateLayoutIcon(LayoutPreset preset)
    {
        var icon = new Grid { Width = 22, Height = 18 };
        for (var row = 0; row < preset.Rows; row++)
            icon.RowDefinitions.Add(new RowDefinition());
        for (var column = 0; column < preset.Columns; column++)
            icon.ColumnDefinitions.Add(new ColumnDefinition());
        foreach (var slot in preset.Slots)
        {
            var panel = new Border
            {
                BorderBrush = Brushes.SteelBlue,
                BorderThickness = new Thickness(1),
                Background = Brushes.AliceBlue,
                Margin = new Thickness(1)
            };
            Grid.SetRow(panel, slot.Row);
            Grid.SetColumn(panel, slot.Column);
            Grid.SetRowSpan(panel, slot.RowSpan);
            Grid.SetColumnSpan(panel, slot.ColumnSpan);
            icon.Children.Add(panel);
        }
        return icon;
    }

    private void SetLayout(int id)
    {
        if (_layout == id)
            return;
        _layout = LayoutPreset.Find(id).Id;
        BuildLayout();
        ScheduleSave();
    }

    private void BuildLayout()
    {
        PaneGrid.Children.Clear();
        PaneGrid.RowDefinitions.Clear();
        PaneGrid.ColumnDefinitions.Clear();
        var layout = LayoutPreset.Find(_layout);
        for (var col = 0; col < layout.Columns; col++)
            PaneGrid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var row = 0; row < layout.Rows; row++)
            PaneGrid.RowDefinitions.Add(new RowDefinition());
        for (var index = 0; index < layout.Slots.Length; index++)
        {
            var pane = _panes[index];
            var slot = layout.Slots[index];
            Grid.SetColumn(pane, slot.Column);
            Grid.SetRow(pane, slot.Row);
            Grid.SetColumnSpan(pane, slot.ColumnSpan);
            Grid.SetRowSpan(pane, slot.RowSpan);
            PaneGrid.Children.Add(pane);
        }

        if (!PaneGrid.Children.Contains(ActivePane))
            ActivePane = _panes[0];
        foreach (var preset in LayoutPreset.All)
        {
            _layoutMenuItems[preset.Id].IsChecked = preset.Id == _layout;
            _layoutButtons[preset.Id].Background = preset.Id == _layout ? Brushes.LightSteelBlue : SystemColors.ControlBrush;
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (control && e.Key == Key.T && shift)
            ActivePane.ReopenClosedTab();
        else if (control && e.Key == Key.T)
            ActivePane.NewTab();
        else if (control && e.Key == Key.K)
            ActivePane.DuplicateCurrentTab();
        else if (control && e.Key == Key.W)
            ActivePane.CloseCurrentTab();
        else if (e.Key == Key.F5)
            ActivePane.RefreshCurrent();
        else
            return;
        e.Handled = true;
    }

    private void NewTab_Click(object sender, RoutedEventArgs e) => ActivePane.NewTab();
    private void DuplicateTab_Click(object sender, RoutedEventArgs e) => ActivePane.DuplicateCurrentTab();
    private void CloseTab_Click(object sender, RoutedEventArgs e) => ActivePane.CloseCurrentTab();
    private void CloseOtherTabs_Click(object sender, RoutedEventArgs e) => ActivePane.CloseOtherTabs();
    private void ReopenTab_Click(object sender, RoutedEventArgs e) => ActivePane.ReopenClosedTab();
    private void RefreshCurrent_Click(object sender, RoutedEventArgs e) => ActivePane.RefreshCurrent();
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

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
