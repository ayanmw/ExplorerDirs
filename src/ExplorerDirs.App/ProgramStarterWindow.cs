using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ExplorerDirs.App;

internal sealed class ProgramStarterWindow : Window
{
    private readonly ProgramStarterStore _store;
    private readonly ApplicationThemeController? _themeController;
    private readonly Func<ProgramStarterContext> _contextProvider;
    private readonly Action<string> _report;
    private readonly ObservableCollection<ProgramStarterEntry> _entries;
    private readonly DataGrid _grid = new() { AutoGenerateColumns = false, CanUserAddRows = false, IsReadOnly = true };
    private readonly Button _add = new() { Content = "添加" };
    private readonly Button _edit = new() { Content = "编辑" };
    private readonly Button _delete = new() { Content = "删除" };
    private readonly Button _import = new() { Content = "导入 Q-Dir 程序项…" };
    private readonly Button _launch = new() { Content = "启动" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private bool _launching;
    private bool _importPreviewOpen;

    public ProgramStarterWindow(ProgramStarterStore store, IEnumerable<ProgramStarterEntry> entries,
        ApplicationThemeController? themeController, Func<ProgramStarterContext> contextProvider, Action<string> report)
    {
        _store = store;
        _themeController = themeController;
        _entries = new ObservableCollection<ProgramStarterEntry>(entries);
        _contextProvider = contextProvider;
        _report = report;
        ApplicationThemeWindowBinding.Attach(this, themeController);
        Title = "程序启动器";
        Width = 760;
        Height = 430;
        MinWidth = 560;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _grid.SelectionChanged += (_, _) => UpdateCommands();
        _grid.Columns.Add(new DataGridTextColumn { Header = "名称", Binding = new System.Windows.Data.Binding(nameof(ProgramStarterEntry.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "程序路径", Binding = new System.Windows.Data.Binding(nameof(ProgramStarterEntry.ExecutablePath)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "参数", Binding = new System.Windows.Data.Binding(nameof(ProgramStarterEntry.Arguments)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "工作目录", Binding = new System.Windows.Data.Binding(nameof(ProgramStarterEntry.WorkingDirectory)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        _grid.ItemsSource = _entries;

        _add.Click += (_, _) => EditEntry(null);
        _edit.Click += (_, _) => EditEntry(_grid.SelectedItem as ProgramStarterEntry);
        _delete.Click += (_, _) => DeleteEntry();
        _import.Click += (_, _) => ImportEntries();
        _launch.Click += (_, _) => LaunchEntry();
        var close = new Button { Content = "关闭", IsCancel = true };
        close.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
        foreach (var button in new[] { _add, _edit, _delete, _import, _launch, close })
        {
            button.Margin = new Thickness(0, 0, 8, 0);
            buttons.Children.Add(button);
        }
        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(_status);
        root.Children.Add(_grid);
        Content = root;
        UpdateCommands();
    }

    private void EditEntry(ProgramStarterEntry? existing)
    {
        var editor = new ProgramStarterEntryEditor(existing, _themeController) { Owner = this };
        if (editor.ShowDialog() != true || editor.Result is not { } value) return;
        var index = existing is null ? -1 : _entries.IndexOf(existing);
        var proposed = _entries.ToList();
        if (index < 0) proposed.Add(value);
        else proposed[index] = value;
        SaveEntries(proposed);
    }

    private void DeleteEntry()
    {
        if (_grid.SelectedItem is not ProgramStarterEntry selected) return;
        SaveEntries(_entries.Where(entry => entry != selected).ToArray());
    }

    private void ImportEntries()
    {
        var picker = new OpenFileDialog { Title = "导入 Q-Dir 程序项", Filter = "Q-Dir 配置 (*.ini)|*.ini|所有文件 (*.*)|*.*" };
        if (picker.ShowDialog(this) != true) return;
        QdrProgramImportPreview preview;
        try { preview = QdrProgramImporter.Parse(QdrProgramImporter.ReadConfiguration(picker.FileName)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            SetStatus($"无法读取配置：{ex.Message}");
            return;
        }

        _importPreviewOpen = true;
        UpdateCommands();
        try
        {
            var remainingCapacity = ProgramStarterStore.MaximumEntries - _entries.Count;
            var dialog = new ProgramStarterImportPreviewWindow(picker.FileName, preview, remainingCapacity, _themeController) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            if (preview.Entries.Count > ProgramStarterStore.MaximumEntries - _entries.Count)
            {
                SetStatus($"导入会超过 {ProgramStarterStore.MaximumEntries} 项上限；请先删除部分项目。");
                return;
            }
            SaveEntries(_entries.Concat(preview.Entries));
        }
        finally
        {
            _importPreviewOpen = false;
            UpdateCommands();
        }
    }

    private void LaunchEntry()
    {
        if (_grid.SelectedItem is not ProgramStarterEntry selected) return;
        _launching = true;
        UpdateCommands();
        try
        {
            var built = ProgramStarterArguments.Build(selected, _contextProvider());
            if (!built.Success)
            {
                SetStatus(built.Error ?? "启动参数无效。");
                return;
            }
            var result = new ProgramStarterLauncher().Start(built.Command!);
            SetStatus(result.Started ? $"已启动：{selected.Name}" : $"启动失败：{result.Error}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or COMException)
        {
            SetStatus($"无法读取当前窗格内容：{ex.Message}");
        }
        finally
        {
            _launching = false;
            UpdateCommands();
        }
    }

    private void SaveEntries(IEnumerable<ProgramStarterEntry> proposed)
    {
        try
        {
            ProgramStarterEntryCommit.Commit(proposed, _store.Save, saved =>
            {
                _entries.Clear();
                foreach (var entry in saved) _entries.Add(entry);
            });
            SetStatus($"已保存 {_entries.Count} 个启动项。配置文件：{_store.Path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"保存失败：{ex.Message}");
        }
    }

    private void UpdateCommands()
    {
        var selected = _grid.SelectedItem as ProgramStarterEntry;
        var state = ProgramStarterCommandState.From(_entries.Count, selected is not null,
            selected is not null && selected.Name.Length > 0 && selected.ExecutablePath.Length > 0,
            _importPreviewOpen, 0,
            ProgramStarterStore.MaximumEntries - _entries.Count, _launching);
        _add.IsEnabled = state.CanAdd;
        _edit.IsEnabled = state.CanEdit;
        _delete.IsEnabled = state.CanDelete;
        _import.IsEnabled = state.CanImport;
        _launch.IsEnabled = state.CanLaunch;
    }

    private void SetStatus(string value)
    {
        _status.Text = value;
        _report(value);
    }
}

internal sealed class ProgramStarterEntryEditor : Window
{
    private readonly TextBox _name = new();
    private readonly TextBox _path = new();
    private readonly TextBox _arguments = new();
    private readonly TextBox _workingDirectory = new();
    private readonly Guid _existingId;
    public ProgramStarterEntry? Result { get; private set; }

    public ProgramStarterEntryEditor(ProgramStarterEntry? entry, ApplicationThemeController? themeController)
    {
        Title = entry is null ? "添加启动项" : "编辑启动项";
        Width = 680;
        Height = 430;
        MinWidth = 560;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ApplicationThemeWindowBinding.Attach(this, themeController);
        _name.Text = entry?.Name ?? string.Empty;
        _existingId = entry?.Id ?? Guid.NewGuid();
        _path.Text = entry?.ExecutablePath ?? string.Empty;
        _arguments.Text = entry?.Arguments ?? string.Empty;
        _workingDirectory.Text = entry?.WorkingDirectory ?? string.Empty;
        var form = new Grid { Margin = new Thickness(12) };
        for (var i = 0; i < 5; i++) form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddField(form, 0, "名称", _name);
        AddField(form, 1, "程序路径", _path);
        AddField(form, 2, "参数", _arguments);
        AddField(form, 3, "工作目录（可选）", _workingDirectory);
        var help = new TextBlock
        {
            Text = "占位符：%curdir% / %curdir1..4%、%sel_files% / %all_files%（可加窗格编号）、%fdir%、%windir%、%sysdir%、%desktop%、%appdat%、%personal%、%drive%。文件列表占位符请作为独立参数。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8)
        };
        Grid.SetRow(help, 4); Grid.SetColumn(help, 1); form.Children.Add(help);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var save = new Button { Content = "保存", IsDefault = true, MinWidth = 85, Margin = new Thickness(0, 8, 8, 0) };
        save.Click += (_, _) => Save();
        var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 85, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(save); buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 5); Grid.SetColumn(buttons, 1); form.Children.Add(buttons);
        Content = form;
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_name.Text) || string.IsNullOrWhiteSpace(_path.Text))
        {
            MessageBox.Show(this, "名称和程序路径不能为空。", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Result = new ProgramStarterEntry(_existingId, _name.Text.Trim(), _path.Text.Trim(),
            _arguments.Text, _workingDirectory.Text.Trim());
        DialogResult = true;
    }

    private static void AddField(Grid grid, int row, string label, TextBox input)
    {
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };
        input.Margin = new Thickness(0, 4, 0, 4);
        Grid.SetRow(text, row); Grid.SetColumn(text, 0); grid.Children.Add(text);
        Grid.SetRow(input, row); Grid.SetColumn(input, 1); grid.Children.Add(input);
    }
}

internal sealed class ProgramStarterImportPreviewWindow : Window
{
    public ProgramStarterImportPreviewWindow(string path, QdrProgramImportPreview preview, int remainingCapacity,
        ApplicationThemeController? themeController)
    {
        Title = "Q-Dir 程序项导入预览";
        Width = 740; Height = 430; MinWidth = 540; MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ApplicationThemeWindowBinding.Attach(this, themeController);
        var root = new DockPanel { Margin = new Thickness(10) };
        var commandState = ProgramStarterCommandState.From(
            ProgramStarterStore.MaximumEntries - remainingCapacity, false, false, true,
            preview.Entries.Count, remainingCapacity, false);
        var exceedsCapacity = preview.Entries.Count > remainingCapacity;
        var capacityMessage = exceedsCapacity
            ? $"当前仅剩 {remainingCapacity} 个名额，超过 {ProgramStarterStore.MaximumEntries} 项上限；请先取消并删除部分项目。"
            : $"当前可用名额：{remainingCapacity}。";
        var source = new TextBlock { Text = $"来源：{path}\n可导入 {preview.Entries.Count} 项；跳过 {preview.SkippedRows.Count} 行。{capacityMessage}\n导入不会修改来源文件，也不会自动运行项目。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(source, Dock.Top); root.Children.Add(source);
        var details = new TextBox { IsReadOnly = true, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.Wrap };
        var lines = preview.Entries.Select(entry => $"导入：{ProgramStarterEntryFormatter.FormatForReview(entry)}")
            .Concat(preview.SkippedRows.Select(row => $"跳过第 {row.LineNumber} 行：{row.Reason} | {row.Text}"));
        details.Text = string.Join(Environment.NewLine, lines);
        root.Children.Add(details);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var apply = new Button { Content = "导入可识别项目", IsDefault = true, IsEnabled = commandState.CanApplyImport, MinWidth = 140, Margin = new Thickness(0, 0, 8, 0) };
        apply.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 80 };
        buttons.Children.Add(apply); buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        Content = root;
    }
}
