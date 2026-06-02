namespace RightMgr.Services;

public sealed record AppWindowPlacement(double Left, double Top, double Width, double Height, bool Maximized);

public static class AppConfigService
{
    private const string FileName = "RightMgr.ini";
    private const string ThemeKey = "theme";
    private const string ContentSplitKey = "content_split";
    private const string WindowLeftKey = "window_left";
    private const string WindowTopKey = "window_top";
    private const string WindowWidthKey = "window_width";
    private const string WindowHeightKey = "window_height";
    private const string WindowMaximizedKey = "window_maximized";

    private static string ConfigDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RightMgr");

    private static string ConfigPath => Path.Combine(ConfigDirectory, FileName);

    public static AppThemeMode? LoadThemeMode()
    {
        return ReadValues().TryGetValue(ThemeKey, out var value) ? ParseThemeMode(value) : null;
    }

    public static void SaveThemeMode(AppThemeMode mode)
    {
        var values = ReadValues();
        values[ThemeKey] = FormatThemeMode(mode);
        WriteValues(values);
    }

    public static double? LoadContentSplit()
    {
        if (!ReadValues().TryGetValue(ContentSplitKey, out var value))
            return null;

        return double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var result)
            ? Math.Clamp(result, 0.25, 0.75)
            : null;
    }

    public static void SaveContentSplit(double value)
    {
        var values = ReadValues();
        values[ContentSplitKey] = Math.Clamp(value, 0.25, 0.75).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        WriteValues(values);
    }

    public static AppWindowPlacement? LoadWindowPlacement()
    {
        var values = ReadValues();
        if (!TryReadDouble(values, WindowLeftKey, out var left)
            || !TryReadDouble(values, WindowTopKey, out var top)
            || !TryReadDouble(values, WindowWidthKey, out var width)
            || !TryReadDouble(values, WindowHeightKey, out var height))
        {
            return null;
        }

        var maximized = values.TryGetValue(WindowMaximizedKey, out var state)
                        && bool.TryParse(state, out var parsed)
                        && parsed;
        return new AppWindowPlacement(left, top, width, height, maximized);
    }

    public static void SaveWindowPlacement(AppWindowPlacement placement)
    {
        var values = ReadValues();
        values[WindowLeftKey] = FormatDouble(placement.Left);
        values[WindowTopKey] = FormatDouble(placement.Top);
        values[WindowWidthKey] = FormatDouble(Math.Max(placement.Width, 1));
        values[WindowHeightKey] = FormatDouble(Math.Max(placement.Height, 1));
        values[WindowMaximizedKey] = placement.Maximized.ToString().ToLowerInvariant();
        WriteValues(values);
    }

    public static AppThemeMode ParseThemeMode(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "light" or "white" => AppThemeMode.Light,
            "dark" or "black" => AppThemeMode.Dark,
            "system" or "auto" => AppThemeMode.System,
            _ => AppThemeMode.System
        };
    }

    public static string FormatThemeMode(AppThemeMode mode)
    {
        return mode switch
        {
            AppThemeMode.Light => "light",
            AppThemeMode.Dark => "dark",
            _ => "system"
        };
    }

    private static Dictionary<string, string> ReadValues()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(ConfigPath))
            return values;

        foreach (var line in File.ReadLines(ConfigPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith('#') || trimmed.StartsWith('['))
                continue;

            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
                continue;

            values[trimmed[..separator].Trim()] = trimmed[(separator + 1)..].Trim();
        }

        return values;
    }

    private static bool TryReadDouble(Dictionary<string, string> values, string key, out double value)
    {
        value = 0;
        return values.TryGetValue(key, out var raw)
               && double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static string FormatDouble(double value)
    {
        return value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void WriteValues(Dictionary<string, string> values)
    {
        Directory.CreateDirectory(ConfigDirectory);
        var lines = values
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => $"{x.Key}={x.Value}");
        File.WriteAllText(ConfigPath, $"[app]{Environment.NewLine}{string.Join(Environment.NewLine, lines)}{Environment.NewLine}");
    }
}
