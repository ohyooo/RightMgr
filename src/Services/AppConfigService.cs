namespace RightMgr.Services;

public static class AppConfigService
{
    private const string FileName = "RightMgr.ini";
    private const string ThemeKey = "theme";

    private static string ConfigDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RightMgr");

    private static string ConfigPath => Path.Combine(ConfigDirectory, FileName);

    public static AppThemeMode? LoadThemeMode()
    {
        if (!File.Exists(ConfigPath))
            return null;

        foreach (var line in File.ReadLines(ConfigPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith('#') || trimmed.StartsWith('['))
                continue;

            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
                continue;

            var key = trimmed[..separator].Trim();
            var value = trimmed[(separator + 1)..].Trim();
            if (key.Equals(ThemeKey, StringComparison.OrdinalIgnoreCase))
                return ParseThemeMode(value);
        }

        return null;
    }

    public static void SaveThemeMode(AppThemeMode mode)
    {
        Directory.CreateDirectory(ConfigDirectory);
        File.WriteAllText(ConfigPath, $"[app]{Environment.NewLine}{ThemeKey}={FormatThemeMode(mode)}{Environment.NewLine}");
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
}
