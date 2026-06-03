using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text.Json.Serialization;

namespace RightMgr.Models;

public enum ContextMenuKind
{
    ShellVerb,
    ShellExHandler
}

public sealed class ContextMenuItemInfo : INotifyPropertyChanged
{
    private bool _isPendingDelete;
    private string? _iconFilePath;
    private ImageSource? _iconImageSource;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string BigCategory { get; set; } = "";
    public string MiddleCategory { get; set; } = "";
    public string SmallCategory { get; set; } = "";
    public string Scope { get; set; } = "";
    public string AppliesTo { get; set; } = "";
    public string SourceRoot { get; set; } = "";
    public string RelativeRegistryPath { get; set; } = "";

    public string MenuName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string RegistryPath { get; set; } = "";
    public string KeyName { get; set; } = "";
    public string? ValueName { get; set; }
    public string? Value { get; set; }

    public ContextMenuKind Kind { get; set; }
    public bool IsEnabled { get; set; } = true;

    public string? Clsid { get; set; }
    public string? ClsidName { get; set; }
    public string? LocalizedString { get; set; }
    public string? IconResource { get; set; }
    public string? IconFilePath
    {
        get => _iconFilePath;
        set
        {
            if (_iconFilePath == value) return;
            _iconFilePath = value;
            _iconImageSource = null;
            OnPropertyChanged(nameof(IconFilePath));
            OnPropertyChanged(nameof(IconImageSource));
        }
    }
    public string? InProcServer32 { get; set; }
    public string? Description { get; set; }
    public bool IsPendingDelete
    {
        get => _isPendingDelete;
        set
        {
            if (_isPendingDelete == value) return;
            _isPendingDelete = value;
            OnPropertyChanged(nameof(IsPendingDelete));
            OnPropertyChanged(nameof(PendingDeleteTextDecorations));
        }
    }

    public string Summary => $"{DisplayName}  |  {AppliesTo}  |  {MiddleCategory}  |  {Scope}";
    public string KindGlyph => Kind == ContextMenuKind.ShellVerb ? "\uE713" : "\uE8B7";
    public string CompactTitle => DisplayName;
    public string CompactSubtitle => $"{AppliesTo}  |  {MiddleCategory}  |  {RegistryPath}";
    [JsonIgnore]
    public ImageSource? IconImageSource => _iconImageSource ??= LoadIconImageSource(_iconFilePath);
    [JsonIgnore]
    public TextDecorationCollection? PendingDeleteTextDecorations => IsPendingDelete ? CreatePendingDeleteDecorations() : null;

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static ImageSource? LoadIconImageSource(string? iconFilePath)
    {
        if (string.IsNullOrWhiteSpace(iconFilePath) || !File.Exists(iconFilePath))
            return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(iconFilePath, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static TextDecorationCollection CreatePendingDeleteDecorations()
    {
        var decoration = new TextDecoration
        {
            Location = TextDecorationLocation.Strikethrough,
            Pen = new Pen(Brushes.IndianRed, 1.8),
            PenThicknessUnit = TextDecorationUnit.FontRecommended
        };

        return [decoration];
    }
}
