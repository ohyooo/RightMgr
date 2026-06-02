using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Text.RegularExpressions;
using RightMgr.Models;
using RightMgr.Services;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Registry = Microsoft.Win32.Registry;

namespace RightMgr.Views;

public partial class MainWindow : Window
{
    private const int WmSettingChange = 0x001A;
    private const int WmThemeChanged = 0x031A;

    private sealed record CategoryItem(string Name, int Count)
    {
        public override string ToString() => $"{Name} ({Count})";
    }

    private List<ContextMenuItemInfo> _items = new();
    private List<ContextMenuItemInfo> _filtered = new();
    private ContextMenuItemInfo? _selected;
    private bool _loading = true;
    private bool _refreshingCategories;

    public MainWindow()
    {
        InitializeComponent();
        ApplySystemTheme();
        LoadData();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(WndProc);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmSettingChange || msg == WmThemeChanged)
            ApplySystemTheme();

        return nint.Zero;
    }

    private void ApplySystemTheme()
    {
        var dark = IsSystemDarkMode();
        SetBrush("Ink", dark ? "#F5F7FA" : "#111827");
        SetBrush("Muted", dark ? "#A6ADBB" : "#6B7280");
        SetBrush("Line", dark ? "#2A3342" : "#D1D5DB");
        SetBrush("Panel", dark ? "#111827" : "#FFFFFF");
        SetBrush("Side", dark ? "#0B1220" : "#111827");
        SetBrush("SideMuted", dark ? "#8E98A8" : "#9CA3AF");
        SetBrush("Accent", dark ? "#60A5FA" : "#1D4ED8");
        SetBrush("Danger", dark ? "#F87171" : "#B91C1C");

        Background = BrushFromHex(dark ? "#0F172A" : "#F3F4F6");
        UpdateLocalControlColors(this, dark);
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

    private static void UpdateLocalControlColors(DependencyObject root, bool dark)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            switch (child)
            {
                case Border border:
                    UpdateBorderColors(border, dark);
                    break;
                case TextBlock textBlock:
                    UpdateTextBlockColors(textBlock, dark);
                    break;
                case TextBox textBox:
                    UpdateTextBoxColors(textBox, dark);
                    break;
                case CheckBox checkBox:
                    checkBox.Foreground = BrushFromHex(dark ? "#F5F7FA" : "#111827");
                    break;
                case Button button:
                    UpdateButtonColors(button, dark);
                    break;
            }

            UpdateLocalControlColors(child, dark);
        }
    }

    private static void UpdateBorderColors(Border border, bool dark)
    {
        if (border.Background is SolidColorBrush bg)
        {
            border.Background = bg.Color switch
            {
                var c when IsColor(c, "#FFFFFF") => BrushFromHex(dark ? "#111827" : "#FFFFFF"),
                var c when IsColor(c, "#F9FAFB") => BrushFromHex(dark ? "#0F172A" : "#F9FAFB"),
                var c when IsColor(c, "#F3F4F6") => BrushFromHex(dark ? "#172033" : "#F3F4F6"),
                _ => border.Background
            };
        }

        if (border.BorderBrush is SolidColorBrush)
            border.BorderBrush = BrushFromHex(dark ? "#2A3342" : "#D1D5DB");
    }

    private static void UpdateTextBoxColors(TextBox textBox, bool dark)
    {
        if (textBox.Background is SolidColorBrush bg && bg.Color.A > 0)
            textBox.Background = BrushFromHex(dark ? "#111827" : "#FFFFFF");

        if (textBox.BorderBrush is SolidColorBrush)
            textBox.BorderBrush = BrushFromHex(dark ? "#374151" : "#D1D5DB");
    }

    private static void UpdateTextBlockColors(TextBlock textBlock, bool dark)
    {
        if (textBlock.Foreground is SolidColorBrush fg)
        {
            if (IsColor(fg.Color, "#111827") || IsColor(fg.Color, "#4B5563") || IsColor(fg.Color, "#6B7280"))
                textBlock.Foreground = BrushFromHex(dark ? "#F5F7FA" : "#111827");
        }
    }

    private static void UpdateButtonColors(Button button, bool dark)
    {
        if (button.Background is SolidColorBrush bg && IsColor(bg.Color, "#FFFFFF"))
            button.Background = BrushFromHex(dark ? "#1F2937" : "#FFFFFF");

        if (button.BorderBrush is SolidColorBrush)
            button.BorderBrush = BrushFromHex(dark ? "#374151" : "#D1D5DB");
    }

    private static bool IsColor(Color color, string hex) => color == (Color)ColorConverter.ConvertFromString(hex);

    private void LoadData()
    {
        _loading = true;
        _items = ContextMenuRegistryScanner.ScanAll();

        HeaderSummaryText.Text = LocalizationService.Format("status_scanned", _items.Count);
        _loading = false;

        RefreshCategories("全部");
        ApplyFilters();
        SelectFirstItem();
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
        OpenDllPathButton.Visibility = HasOpenableFilePath(item.InProcServer32) ? Visibility.Visible : Visibility.Collapsed;
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

        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.T("dialog_icon_title"),
            Filter = LocalizationService.T("dialog_icon_filter"),
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        IconResourceBox.Text = dialog.FileName;
        ShowIcon(ShellIconService.GetPngIconPath(dialog.FileName));
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
