using System.Globalization;
using System.Reflection;
using System.Windows.Markup;

namespace RightMgr.Services;

public static class LocalizationService
{
    private const string FallbackLanguage = "zh-Hans";
    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> LazyStrings = new(LoadStrings);

    public static string Language { get; set; } = PickLanguage();

    public static string T(string key)
    {
        var strings = LazyStrings.Value;
        if (strings.TryGetValue(key, out var row))
        {
            if (row.TryGetValue(Language, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;

            if (row.TryGetValue(FallbackLanguage, out value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return key;
    }

    public static string Format(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);

    private static string PickLanguage()
    {
        var name = CultureInfo.CurrentUICulture.Name;
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return "zh-Hans";

        return "en-US";
    }

    private static Dictionary<string, Dictionary<string, string>> LoadStrings()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "i18n.csv");
        if (!File.Exists(path))
            path = Path.Combine(AppContext.BaseDirectory, "i18n.csv");

        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var lines = File.Exists(path) ? File.ReadAllLines(path) : ReadEmbeddedStrings();
        if (lines.Length == 0)
            return result;

        var headers = ParseCsvLine(lines[0]);
        for (var i = 1; i < lines.Length; i++)
        {
            var cells = ParseCsvLine(lines[i]);
            if (cells.Count == 0 || string.IsNullOrWhiteSpace(cells[0]))
                continue;

            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var c = 1; c < headers.Count && c < cells.Count; c++)
                row[headers[c]] = cells[c];

            result[cells[0]] = row;
        }

        return result;
    }

    private static string[] ReadEmbeddedStrings()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("RightMgr.Resources.i18n.csv");
        if (stream == null)
            return [];

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split(["\r\n", "\n"], StringSplitOptions.None);
    }

    private static List<string> ParseCsvLine(string line)
    {
        var cells = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == ',' && !inQuotes)
            {
                cells.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        cells.Add(current.ToString());
        return cells;
    }
}

public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => LocalizationService.T(Key);
}
