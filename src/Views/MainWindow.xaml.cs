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
        var query = SearchBox.Text?.Trim() ?? "";

        IEnumerable<ContextMenuItemInfo> items = ApplyTypeAndSearchFilters(_items, includeShellVerb, includeShellEx, query);

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
        string query)
    {
        var items = source.Where(x =>
            (includeShellVerb && x.Kind == ContextMenuKind.ShellVerb)
            || (includeShellEx && x.Kind == ContextMenuKind.ShellExHandler));

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
        var countSource = ApplyTypeAndSearchFilters(_items, includeShellVerb, includeShellEx, query).ToList();
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

        SearchBox.Text = extension;
        SearchNameBox.IsChecked = false;
        SearchCategoryBox.IsChecked = true;
        SearchRegistryBox.IsChecked = false;
        SearchValueBox.IsChecked = false;
        SearchComBox.IsChecked = false;
        UseRegexBox.IsChecked = false;
        ApplyFilters();
        SelectFirstItem();
        StatusText.Text = $"已按后缀名 {extension} 过滤。";
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
        OpenClsidButton.Visibility = string.IsNullOrWhiteSpace(item.Clsid) ? Visibility.Collapsed : Visibility.Visible;
        OpenDllPathButton.Visibility = string.IsNullOrWhiteSpace(item.InProcServer32) ? Visibility.Collapsed : Visibility.Visible;
        OpenIconPathButton.Visibility = HasOpenableFilePath(item.IconResource) ? Visibility.Visible : Visibility.Collapsed;
        EnableSwitch.IsChecked = item.IsEnabled;
        EnableSwitch.IsEnabled = true;
        SaveButton.IsEnabled = true;
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
        DeleteButton.IsEnabled = false;
    }

    private void OpenRegistryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null)
            return;

        OpenRegistryPath(_selected.RegistryPath);
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

    private void PrintAllButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = ContextMenuRegistryScanner.FormatAll(_items);
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var path = Path.Combine(desktop, "RightMgr-print-all.txt");
            File.WriteAllText(path, text);
            Clipboard.SetText(text);
            StatusText.Text = $"已打印全部 {_items.Count} 项到 {path}，并复制到剪贴板。";
            MessageBox.Show(this, $"已输出：\n{path}", LocalizationService.T("dialog_notice"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, LocalizationService.T("dialog_notice"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
