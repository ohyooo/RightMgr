using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Microsoft.Win32;
using RightMgr.Models;
using RightMgr.Services;
using Registry = Microsoft.Win32.Registry;

namespace RightMgr.Views;

public partial class MainWindow : Window
{
    private const int WmSettingChange = 0x001A;
    private const int WmThemeChanged = 0x031A;
    private const int DwmwaUseImmersiveDarkMode = 20;

    private sealed record CategoryItem(string Name, int Count)
    {
        public override string ToString() => $"{Name} ({Count})";
    }

    private sealed record ExportScope(string Label, IReadOnlyList<ContextMenuItemInfo> Items);

    private List<ContextMenuItemInfo> _items = new();
    private List<ContextMenuItemInfo> _filtered = new();
    private ContextMenuItemInfo? _selected;
    private AppThemeMode _themeMode;
    private AppThemePalette _palette = AppThemePalette.Light;
    private bool _loading = true;
    private bool _refreshingCategories;

    public MainWindow(AppThemeMode themeMode = AppThemeMode.System)
    {
        _themeMode = themeMode;
        InitializeComponent();
        AllowDrop = true;
        ApplyWindowPlacement();
        ApplyContentSplit();
        ApplyTheme();
        LoadData();
        Loaded += (_, _) => ApplyTheme();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(WndProc);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        UpdateMaximizeButtonIcon();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        SaveWindowPlacement();
        base.OnClosing(e);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (_themeMode == AppThemeMode.System && (msg == WmSettingChange || msg == WmThemeChanged))
            ApplyTheme();

        return nint.Zero;
    }

    private void ApplyTheme()
    {
        var dark = _themeMode switch
        {
            AppThemeMode.Light => false,
            AppThemeMode.Dark => true,
            _ => IsSystemDarkMode()
        };
        _palette = dark ? AppThemePalette.Dark : AppThemePalette.Light;
        ApplyPalette(_palette);
        Background = BrushFromHex(_palette.Window);
        UpdateThemeButtonIcon();
        ApplyWindowChromeTheme(dark);
    }

    private void ApplyContentSplit()
    {
        var split = AppConfigService.LoadContentSplit() ?? 0.46;
        Loaded += (_, _) =>
        {
            var total = ListPaneColumn.ActualWidth + DetailPaneColumn.ActualWidth;
            if (total <= 0)
                return;

            ListPaneColumn.Width = new GridLength(Math.Clamp(total * split, ListPaneColumn.MinWidth, total - DetailPaneColumn.MinWidth));
            DetailPaneColumn.Width = new GridLength(1, GridUnitType.Star);
        };
    }

    private void ApplyWindowPlacement()
    {
        var placement = AppConfigService.LoadWindowPlacement();
        if (placement == null)
            return;

        Width = Math.Max(MinWidth, placement.Width);
        Height = Math.Max(MinHeight, placement.Height);
        Left = placement.Left;
        Top = placement.Top;
        WindowStartupLocation = WindowStartupLocation.Manual;

        if (placement.Maximized)
            WindowState = WindowState.Maximized;
    }

    private void SaveWindowPlacement()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        var maximized = WindowState == WindowState.Maximized;
        AppConfigService.SaveWindowPlacement(new AppWindowPlacement(
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            maximized));
    }

    private static bool IsSystemDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 1)) == 0;
        }
        catch
        {
            return false;
        }
    }

    private void ApplyPalette(AppThemePalette palette)
    {
        SetBrush("WindowBg", palette.Window);
        SetBrush("Surface", palette.Surface);
        SetBrush("SurfaceAlt", palette.SurfaceAlt);
        SetBrush("SurfaceRaised", palette.SurfaceRaised);
        SetBrush("ControlBg", palette.Control);
        SetBrush("ControlHover", palette.ControlHover);
        SetBrush("Ink", palette.Text);
        SetBrush("Muted", palette.TextMuted);
        SetBrush("Subtle", palette.TextSubtle);
        SetBrush("Line", palette.Border);
        SetBrush("Panel", palette.Surface);
        SetBrush("Side", palette.Sidebar);
        SetBrush("SideMuted", palette.SidebarText);
        SetBrush("SideSubtle", palette.SidebarTextMuted);
        SetBrush("SideSelection", palette.SidebarSelection);
        SetBrush("SideHover", palette.SidebarHover);
        SetBrush("Accent", palette.Accent);
        SetBrush("AccentText", palette.AccentText);
        SetBrush("Danger", palette.Danger);
    }

    private void SetBrush(string key, string color)
    {
        if (Resources[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = (Color)ColorConverter.ConvertFromString(color);
            return;
        }

        Resources[key] = BrushFromHex(color);
    }

    private static SolidColorBrush BrushFromHex(string color) => new((Color)ColorConverter.ConvertFromString(color));

    private void ApplyWindowChromeTheme(bool dark)
    {
        if (PresentationSource.FromVisual(this) is not HwndSource source)
            return;

        var enabled = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(source.Handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void DragArea_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            e.Handled = true;
            return;
        }

        try
        {
            DragMove();
            e.Handled = true;
        }
        catch (InvalidOperationException)
        {
            // Child controls can bubble mouse events after the button state changes.
        }
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleWindowState();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        UpdateMaximizeButtonIcon();
    }

    private void UpdateMaximizeButtonIcon()
    {
        if (MaximizeButton == null)
            return;

        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _themeMode = _themeMode switch
        {
            AppThemeMode.System => AppThemeMode.Light,
            AppThemeMode.Light => AppThemeMode.Dark,
            _ => AppThemeMode.System
        };

        AppConfigService.SaveThemeMode(_themeMode);
        ApplyTheme();
    }

    private void UpdateThemeButtonIcon()
    {
        if (ThemeButtonIcon == null || ThemeButton == null)
            return;

        ThemeButtonIcon.Text = _themeMode switch
        {
            AppThemeMode.Light => "\uE706",
            AppThemeMode.Dark => "\uE708",
            _ => "\uE771"
        };
        ThemeButton.ToolTip = _themeMode switch
        {
            AppThemeMode.Light => "白色主题",
            AppThemeMode.Dark => "黑色主题",
            _ => "跟随系统"
        };
    }

    private void LoadData()
    {
        _loading = true;
        _items = ContextMenuRegistryScanner.ScanAll();

        HeaderSummaryText.Text = LocalizationService.Format("status_scanned", _items.Count);
        _loading = false;

        RefreshCategories("全部");
        ApplyFilters();
        SelectFirstItem();
        Dispatcher.BeginInvoke(ApplyTheme, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void ApplyFilters()
    {
        if (_loading) return;

        var category = GetSelectedCategory();
        var includeShellVerb = ShellVerbFilterBox.IsChecked == true;
        var includeShellEx = ShellExFilterBox.IsChecked == true;
        var includeEnabled = EnabledFilterBox.IsChecked == true;
        var includeDisabled = DisabledFilterBox.IsChecked == true;
        var query = SearchBox.Text?.Trim() ?? "";

        IEnumerable<ContextMenuItemInfo> items = ApplyTypeAndSearchFilters(
            _items,
            includeShellVerb,
            includeShellEx,
            includeEnabled,
            includeDisabled,
            query);

        RefreshCategories(category);

        if (category != "全部")
            items = items.Where(x => x.BigCategory.Equals(category, StringComparison.OrdinalIgnoreCase));

        var currentListQuery = CurrentListSearchBox.Text?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(currentListQuery))
            items = items.Where(x => MatchesCurrentListSearch(x, currentListQuery));

        _filtered = items.ToList();
        ItemsList.ItemsSource = _filtered;
        ListTitleText.Text = category;
        CountText.Text = $"{_filtered.Count} 项";
        StatusText.Text = BuildStatusText();
    }

    private IEnumerable<ContextMenuItemInfo> ApplyTypeAndSearchFilters(
        IEnumerable<ContextMenuItemInfo> source,
        bool includeShellVerb,
        bool includeShellEx,
        bool includeEnabled,
        bool includeDisabled,
        string query)
    {
        var items = source.Where(x =>
            (includeShellVerb && x.Kind == ContextMenuKind.ShellVerb)
            || (includeShellEx && x.Kind == ContextMenuKind.ShellExHandler));

        items = items.Where(x =>
            (includeEnabled && x.IsEnabled)
            || (includeDisabled && !x.IsEnabled));

        if (!string.IsNullOrWhiteSpace(query))
            items = items.Where(x => Matches(x, query));

        return items;
    }

    private void RefreshCategories(string selectedCategory)
    {
        if (_loading) return;

        var includeShellVerb = ShellVerbFilterBox.IsChecked == true;
        var includeShellEx = ShellExFilterBox.IsChecked == true;
        var query = SearchBox.Text?.Trim() ?? "";
        var includeEnabled = EnabledFilterBox.IsChecked == true;
        var includeDisabled = DisabledFilterBox.IsChecked == true;
        var countSource = ApplyTypeAndSearchFilters(_items, includeShellVerb, includeShellEx, includeEnabled, includeDisabled, query).ToList();
        var categories = countSource
            .GroupBy(x => x.BigCategory, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CategoryItem(g.Key, g.Count()))
            .OrderBy(x => x.Name)
            .Prepend(new CategoryItem("全部", countSource.Count))
            .ToList();

        _refreshingCategories = true;
        CategoryList.ItemsSource = categories;
        CategoryList.SelectedItem = categories.FirstOrDefault(x => x.Name.Equals(selectedCategory, StringComparison.OrdinalIgnoreCase))
                                    ?? categories.FirstOrDefault();
        _refreshingCategories = false;
    }

    private string GetSelectedCategory()
    {
        return CategoryList.SelectedItem is CategoryItem item ? item.Name : "全部";
    }

    private bool Matches(ContextMenuItemInfo item, string query)
    {
        var values = EnumerateSearchValues(
            item,
            SearchNameBox.IsChecked == true,
            SearchCategoryBox.IsChecked == true,
            SearchRegistryBox.IsChecked == true,
            SearchValueBox.IsChecked == true,
            SearchComBox.IsChecked == true);
        if (UseRegexBox.IsChecked == true)
        {
            try
            {
                return values.Any(value => value != null && Regex.IsMatch(value, query, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
            }
            catch (ArgumentException)
            {
                StatusText.Text = LocalizationService.T("status_regex_invalid");
                return false;
            }
        }

        return values.Any(value => Contains(value, query));
    }

    private bool MatchesCurrentListSearch(ContextMenuItemInfo item, string query)
    {
        var values = EnumerateSearchValues(
            item,
            CurrentSearchNameBox.IsChecked == true,
            CurrentSearchCategoryBox.IsChecked == true,
            CurrentSearchRegistryBox.IsChecked == true,
            CurrentSearchValueBox.IsChecked == true,
            CurrentSearchComBox.IsChecked == true);

        if (CurrentUseRegexBox.IsChecked == true)
        {
            try
            {
                return values.Any(value => value != null && Regex.IsMatch(value, query, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
            }
            catch (ArgumentException)
            {
                StatusText.Text = LocalizationService.T("status_regex_invalid");
                return false;
            }
        }

        return values.Any(value => Contains(value, query));
    }

    private static IEnumerable<string?> EnumerateSearchValues(
        ContextMenuItemInfo item,
        bool includeName,
        bool includeCategory,
        bool includeRegistry,
        bool includeValue,
        bool includeCom)
    {
        if (includeName)
        {
            yield return item.DisplayName;
            yield return item.MenuName;
            yield return item.Description;
        }

        if (includeCategory)
        {
            yield return item.BigCategory;
            yield return item.MiddleCategory;
            yield return item.SmallCategory;
            yield return item.AppliesTo;
            yield return item.Scope;
            yield return item.SourceRoot;
        }

        if (includeRegistry)
        {
            yield return item.RegistryPath;
            yield return item.RelativeRegistryPath;
            yield return item.KeyName;
            yield return item.ValueName;
        }

        if (includeValue)
            yield return item.Value;

        if (includeCom)
        {
            yield return item.Clsid;
            yield return item.ClsidName;
            yield return item.LocalizedString;
            yield return item.InProcServer32;
            yield return item.IconResource;
        }
    }

    private static bool Contains(string? value, string query)
    {
        return value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
    }

    private string BuildStatusText()
    {
        var shell = _filtered.Count(x => x.Kind == ContextMenuKind.ShellVerb);
        var shellex = _filtered.Count(x => x.Kind == ContextMenuKind.ShellExHandler);
        var disabled = _filtered.Count(x => !x.IsEnabled);
        return LocalizationService.Format("status_summary", shell, shellex, disabled);
    }

    private void SelectFirstItem()
    {
        if (_filtered.Count == 0)
        {
            ClearDetail();
            return;
        }

        ItemsList.SelectedIndex = 0;
    }

    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _refreshingCategories) return;
        ApplyFilters();
        SelectFirstItem();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilters();
        SelectFirstItem();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = GetDroppedFileExtension(e) == null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        var extension = GetDroppedFileExtension(e);
        if (extension == null)
            return;

        var query = extension.TrimStart('.');
        _loading = true;
        UseRegexBox.IsChecked = false;
        SearchNameBox.IsChecked = true;
        SearchCategoryBox.IsChecked = true;
        SearchRegistryBox.IsChecked = true;
        SearchValueBox.IsChecked = true;
        SearchComBox.IsChecked = true;
        SearchBox.Text = query;
        _loading = false;

        RefreshCategories("按扩展名");
        ApplyFilters();
        SelectFirstItem();
        StatusText.Text = $"已按后缀名 {query} 过滤。";
        e.Handled = true;
    }

    private static string? GetDroppedFileExtension(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return null;

        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        var file = files?.FirstOrDefault(File.Exists);
        var extension = file == null ? null : Path.GetExtension(file);
        return string.IsNullOrWhiteSpace(extension) ? null : extension;
    }

    private void TypeFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        ApplyFilters();
        SelectFirstItem();
    }

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        FilterPopup.IsOpen = !FilterPopup.IsOpen;
    }

    private void SearchOptions_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        ApplyFilters();
        SelectFirstItem();
    }

    private void CurrentListSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilters();
        SelectFirstItem();
    }

    private void CurrentListFilterButton_Click(object sender, RoutedEventArgs e)
    {
        CurrentListFilterPopup.IsOpen = !CurrentListFilterPopup.IsOpen;
    }

    private void CurrentSearchOptions_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        ApplyFilters();
        SelectFirstItem();
    }

    private void ContentSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        var total = ListPaneColumn.ActualWidth + DetailPaneColumn.ActualWidth;
        if (total <= 0)
            return;

        ListPaneColumn.Width = new GridLength(ListPaneColumn.ActualWidth);
        DetailPaneColumn.Width = new GridLength(1, GridUnitType.Star);
        AppConfigService.SaveContentSplit(ListPaneColumn.ActualWidth / total);
    }

    private void ItemsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ItemsList.SelectedItem is ContextMenuItemInfo item)
            ShowDetail(item);
    }

    private void ShowDetail(ContextMenuItemInfo item)
    {
        _loading = true;
        _selected = item;

        MenuNameText.Text = item.DisplayName;
        MenuNameText.TextDecorations = item.IsPendingDelete ? TextDecorations.Strikethrough : null;
        DescriptionText.Text = item.Description ?? item.ClsidName ?? item.MenuName;
        CategoryBox.Text = $"{item.BigCategory} / {item.AppliesTo} / {item.MiddleCategory}";
        ScopeBox.Text = $"{item.Scope} ({item.SourceRoot})";
        RegistryPathBox.Text = item.RegistryPath;
        KeyNameBox.Text = item.KeyName;
        ValueNameBox.Text = item.ValueName ?? "";
        ValueBox.Text = item.Value ?? "";
        ClsidBox.Text = item.Clsid ?? "";
        DllPathBox.Text = item.InProcServer32 ?? "";
        IconResourceBox.Text = item.IconResource ?? "";
        EnableSwitch.IsChecked = item.IsEnabled;
        OpenClsidButton.Visibility = !string.IsNullOrWhiteSpace(item.Clsid) ? Visibility.Visible : Visibility.Collapsed;
        OpenDllPathButton.Visibility = !string.IsNullOrWhiteSpace(item.InProcServer32) ? Visibility.Visible : Visibility.Collapsed;
        OpenIconPathButton.Visibility = HasOpenableFilePath(item.IconResource) ? Visibility.Visible : Visibility.Collapsed;
        EnableSwitch.IsEnabled = true;
        SaveButton.IsEnabled = true;
        SaveButton.Content = LocalizationService.T("action_save");
        DeleteButton.IsEnabled = true;
        DeleteButton.Content = item.IsPendingDelete ? LocalizationService.T("action_cancel_delete") : LocalizationService.T("action_delete");

        if (!string.IsNullOrWhiteSpace(item.IconFilePath) && File.Exists(item.IconFilePath))
        {
            ShowIcon(item.IconFilePath);
        }
        else
        {
            HeaderIcon.Source = null;
            HeaderIcon.Visibility = Visibility.Collapsed;
        }

        _loading = false;
    }

    private void ClearDetail()
    {
        _selected = null;
        MenuNameText.Text = LocalizationService.T("detail_no_match_title");
        DescriptionText.Text = LocalizationService.T("detail_no_match_subtitle");
        CategoryBox.Text = "";
        ScopeBox.Text = "";
        RegistryPathBox.Text = "";
        KeyNameBox.Text = "";
        ValueNameBox.Text = "";
        ValueBox.Text = "";
        ClsidBox.Text = "";
        DllPathBox.Text = "";
        IconResourceBox.Text = "";
        OpenClsidButton.Visibility = Visibility.Collapsed;
        OpenDllPathButton.Visibility = Visibility.Collapsed;
        OpenIconPathButton.Visibility = Visibility.Collapsed;
        HeaderIcon.Source = null;
        HeaderIcon.Visibility = Visibility.Collapsed;
        EnableSwitch.IsEnabled = false;
        SaveButton.IsEnabled = false;
        SaveButton.Content = LocalizationService.T("action_save");
        DeleteButton.IsEnabled = false;
    }

    private void OpenRegistryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null)
            return;

        OpenRegistryPath(_selected.RegistryPath);
    }

    private void ExportRegistryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null || string.IsNullOrWhiteSpace(_selected.RegistryPath))
            return;

        try
        {
            var dialog = new SaveFileDialog
            {
                Title = LocalizationService.T("dialog_export_reg_title"),
                Filter = LocalizationService.T("dialog_export_reg_filter"),
                FileName = BuildRegFileName(_selected),
                AddExtension = true,
                DefaultExt = ".reg",
                OverwritePrompt = true
            };

            if (dialog.ShowDialog(this) != true)
                return;

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "reg.exe",
                Arguments = $"export \"{_selected.RegistryPath}\" \"{dialog.FileName}\" /y",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null)
                throw new InvalidOperationException("无法启动 reg.exe");

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output : error);

            StatusText.Text = LocalizationService.Format("status_exported_reg", _selected.DisplayName, dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, LocalizationService.T("dialog_notice"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenClsidRegistryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null || string.IsNullOrWhiteSpace(_selected.Clsid))
            return;

        OpenRegistryPath($@"HKEY_CLASSES_ROOT\CLSID\{_selected.Clsid}");
    }

    private void OpenRegistryPath(string registryPath)
    {
        try
        {
            using var regedit = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit");
            regedit?.SetValue("LastKey", registryPath);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "regedit.exe",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, LocalizationService.T("dialog_notice"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenDllPathButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFilePath(DllPathBox.Text);
    }

    private void OpenIconPathButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFilePath(ShellResourceResolver.ExtractFilePathFromResource(IconResourceBox.Text) ?? IconResourceBox.Text);
    }

    private void OpenFilePath(string? path)
    {
        path = ShellResourceResolver.ExtractFilePathFromResource(path) ?? Environment.ExpandEnvironmentVariables(path?.Trim().Trim('"') ?? "");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            MessageBox.Show(this, path, LocalizationService.T("dialog_notice"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, LocalizationService.T("dialog_notice"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static bool HasOpenableFilePath(string? path)
    {
        return !string.IsNullOrWhiteSpace(ShellResourceResolver.ExtractFilePathFromResource(path));
    }

    private static string BuildRegFileName(ContextMenuItemInfo item)
    {
        var name = string.IsNullOrWhiteSpace(item.DisplayName) ? item.KeyName : item.DisplayName;
        var invalidChars = Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(ch => invalidChars.Contains(ch) ? '-' : ch).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "export.reg" : $"{safe}.reg";
    }

    private void ChooseIconButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null)
            return;

        var iconResource = NativeIconDialog.Show(this, IconResourceBox.Text);
        if (iconResource == null)
            return;

        IconResourceBox.Text = iconResource;
        ShowIcon(ShellIconService.GetPngIconPath(iconResource));
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null)
            return;

        try
        {
            if (_selected.IsPendingDelete)
            {
                ContextMenuRegistryEditor.Delete(_selected);
                _items.Remove(_selected);
                ApplyFilters();
                SelectFirstItem();
                StatusText.Text = LocalizationService.T("status_deleted");
                return;
            }

            var iconResource = IconResourceBox.Text.Trim();
            if (!string.Equals(iconResource, _selected.IconResource ?? "", StringComparison.Ordinal))
            {
                ContextMenuRegistryEditor.SetIcon(_selected, iconResource);
                _selected.IconResource = iconResource;
                _selected.IconFilePath = ShellIconService.GetPngIconPath(iconResource);
                ShowIcon(_selected.IconFilePath);
            }

            var value = ValueBox.Text;
            if (!string.Equals(value, _selected.Value ?? "", StringComparison.Ordinal))
            {
                ContextMenuRegistryEditor.SetValue(_selected, value);
                _selected.Value = value;
            }

            StatusText.Text = LocalizationService.T("status_saved");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, LocalizationService.T("dialog_notice"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowIcon(string? iconPath)
    {
        if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
        {
            HeaderIcon.Source = new BitmapImage(new Uri(iconPath));
            HeaderIcon.Visibility = Visibility.Visible;
            return;
        }

        HeaderIcon.Source = null;
        HeaderIcon.Visibility = Visibility.Collapsed;
    }

    private void EnableSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _selected == null)
            return;

        try
        {
            if (EnableSwitch.IsChecked == true)
            {
                ContextMenuRegistryEditor.Enable(_selected);
                _selected.IsEnabled = true;
            }
            else
            {
                ContextMenuRegistryEditor.Disable(_selected);
                _selected.IsEnabled = false;
            }

            StatusText.Text = BuildStatusText();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, LocalizationService.T("dialog_notice"), MessageBoxButton.OK, MessageBoxImage.Warning);
            _loading = true;
            EnableSwitch.IsChecked = _selected.IsEnabled;
            _loading = false;
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null)
            return;

        _selected.IsPendingDelete = !_selected.IsPendingDelete;
        MenuNameText.TextDecorations = _selected.IsPendingDelete ? TextDecorations.Strikethrough : null;
        DeleteButton.Content = _selected.IsPendingDelete ? LocalizationService.T("action_cancel_delete") : LocalizationService.T("action_delete");
        SaveButton.Content = LocalizationService.T("action_save");
        ItemsList.Items.Refresh();
        StatusText.Text = _selected.IsPendingDelete ? LocalizationService.T("status_pending_delete") : LocalizationService.T("status_cancel_delete");
    }

    private void CopyAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null)
            return;

        Clipboard.SetText(FormatItem(_selected));
        StatusText.Text = LocalizationService.T("status_copy");
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var scope = ShowExportScopeDialog();
            if (scope == null)
                return;

            var dialog = new SaveFileDialog
            {
                Title = LocalizationService.T("dialog_export_title"),
                Filter = LocalizationService.T("dialog_export_filter"),
                FileName = BuildExportFileName(scope.Label),
                AddExtension = true,
                DefaultExt = ".txt",
                OverwritePrompt = true
            };

            if (dialog.ShowDialog(this) != true)
                return;

            File.WriteAllText(dialog.FileName, ContextMenuRegistryScanner.FormatAll(scope.Items));
            StatusText.Text = LocalizationService.Format("status_exported", scope.Items.Count, dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, LocalizationService.T("dialog_notice"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private ExportScope? ShowExportScopeDialog()
    {
        var scopes = BuildExportScopes();
        var dialog = new Window
        {
            Title = LocalizationService.T("dialog_export_title"),
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            Background = Resources["Surface"] as Brush,
            FontFamily = FontFamily,
            Padding = new Thickness(18)
        };

        var root = new StackPanel { MinWidth = 320 };
        root.Children.Add(new TextBlock
        {
            Text = LocalizationService.T("dialog_export_scope"),
            Foreground = Resources["Subtle"] as Brush,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var comboBox = new ComboBox
        {
            ItemsSource = scopes,
            SelectedIndex = 0,
            DisplayMemberPath = nameof(ExportScope.Label),
            MinHeight = 32,
            Margin = new Thickness(0, 0, 0, 16)
        };
        root.Children.Add(comboBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancelButton = new Button
        {
            Content = LocalizationService.T("dialog_export_cancel"),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var exportButton = new Button
        {
            Content = LocalizationService.T("dialog_export_save"),
            Style = (Style)Resources["PrimaryButton"]
        };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(exportButton);
        root.Children.Add(buttons);

        ExportScope? selected = null;
        cancelButton.Click += (_, _) => dialog.Close();
        exportButton.Click += (_, _) =>
        {
            selected = comboBox.SelectedItem as ExportScope;
            dialog.Close();
        };

        dialog.Content = root;
        dialog.ShowDialog();
        return selected;
    }

    private List<ExportScope> BuildExportScopes()
    {
        var scopes = new List<ExportScope>
        {
            new(LocalizationService.T("dialog_export_all"), _items),
            new(LocalizationService.T("dialog_export_current"), _filtered)
        };

        scopes.AddRange(_items
            .GroupBy(x => x.BigCategory, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key)
            .Select(g => new ExportScope(LocalizationService.Format("dialog_export_category", g.Key), g.ToList())));

        return scopes;
    }

    private static string BuildExportFileName(string scopeLabel)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeScope = new string(scopeLabel.Select(ch => invalidChars.Contains(ch) ? '-' : ch).ToArray());
        return $"RightMgr-{safeScope}.txt";
    }

    private void SourceCodeLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, LocalizationService.T("dialog_notice"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadData();
    }

    private static string FormatItem(ContextMenuItemInfo item)
    {
        return $"""
{LocalizationService.T("format_menu_name")}: {item.DisplayName}
{LocalizationService.T("format_raw_name")}: {item.MenuName}
{LocalizationService.T("format_category")}: {item.BigCategory} / {item.AppliesTo} / {item.MiddleCategory}
{LocalizationService.T("format_scope")}: {item.Scope} ({item.SourceRoot})
{LocalizationService.T("format_registry")}: {item.RegistryPath}
{LocalizationService.T("format_key")}: {item.KeyName}
{LocalizationService.T("format_value_name")}: {item.ValueName}
{LocalizationService.T("format_value")}: {item.Value}
CLSID: {item.Clsid}
{LocalizationService.T("format_clsid_name")}: {item.ClsidName}
{LocalizationService.T("format_localized")}: {item.LocalizedString}
{LocalizationService.T("format_dll")}: {item.InProcServer32}
{LocalizationService.T("format_icon")}: {item.IconResource}
{LocalizationService.T("format_enabled")}: {item.IsEnabled}
""";
    }
}
