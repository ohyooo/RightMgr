using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace RightMgr.Services;

public static class ShellResourceResolver
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(
        string pszSource,
        char[] pszOutBuf,
        uint cchOutBuf,
        nint ppvReserved);

    public static string ResolveDisplayName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        raw = raw.Trim();

        if (raw.StartsWith("@", StringComparison.Ordinal))
        {
            var text = TryLoadIndirectString(raw);
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        if (IsClsid(raw))
        {
            var name = ResolveClsidName(raw);
            return string.IsNullOrWhiteSpace(name) ? $"Shell 扩展：{raw}" : name;
        }

        return raw;
    }

    public static string? ResolveClsidDisplayName(string? raw)
    {
        var clsid = ExtractClsid(raw);
        return clsid == null ? null : ResolveClsidName(clsid);
    }

    public static string? TryLoadIndirectString(string raw)
    {
        var buffer = new char[2048];
        var hr = SHLoadIndirectString(raw, buffer, (uint)buffer.Length, nint.Zero);
        if (hr != 0)
            return null;

        return new string(buffer).TrimEnd('\0');
    }

    public static string? ResolveClsidName(string clsid)
    {
        using var key = Registry.ClassesRoot.OpenSubKey($@"CLSID\{clsid}");
        if (key != null)
        {
            var localized = key.GetValue("LocalizedString")?.ToString();
            if (!string.IsNullOrWhiteSpace(localized))
            {
                var text = ResolveDisplayName(localized);
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            var defaultName = key.GetValue(null)?.ToString();
            if (!string.IsNullOrWhiteSpace(defaultName))
                return defaultName;
        }

        return ResolveApprovedShellExtensionName(clsid);
    }

    private static string? ResolveApprovedShellExtensionName(string clsid)
    {
        using var approved = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved");
        var name = approved?.GetValue(clsid)?.ToString();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    public static string? ResolveClsidLocalizedString(string clsid)
    {
        using var key = Registry.ClassesRoot.OpenSubKey($@"CLSID\{clsid}");
        return key?.GetValue("LocalizedString")?.ToString();
    }

    public static string? ResolveClsidIcon(string clsid)
    {
        using var key = Registry.ClassesRoot.OpenSubKey($@"CLSID\{clsid}\DefaultIcon");
        return key?.GetValue(null)?.ToString();
    }

    public static string? ResolveInProcServer32(string clsid)
    {
        using var key = Registry.ClassesRoot.OpenSubKey($@"CLSID\{clsid}\InprocServer32");
        return key?.GetValue(null)?.ToString();
    }

    public static string? ExtractClsid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();
        return IsClsid(value) ? value : null;
    }

    public static bool IsClsid(string value)
    {
        return value.StartsWith("{", StringComparison.Ordinal)
               && value.EndsWith("}", StringComparison.Ordinal)
               && Guid.TryParse(value.Trim('{', '}'), out _);
    }

    public static string? ExtractFilePathFromResource(string? resource)
    {
        if (string.IsNullOrWhiteSpace(resource))
            return null;

        var s = resource.Trim().Trim('"');
        if (s.StartsWith("@", StringComparison.Ordinal))
            s = s[1..];

        var comma = s.LastIndexOf(',');
        if (comma > 0)
            s = s[..comma];

        s = Environment.ExpandEnvironmentVariables(s.Trim().Trim('"'));
        return File.Exists(s) ? s : null;
    }

    public static int ExtractIconIndex(string? resource)
    {
        if (string.IsNullOrWhiteSpace(resource))
            return 0;

        var s = resource.Trim().Trim('"');
        var comma = s.LastIndexOf(',');
        return comma > 0 && int.TryParse(s[(comma + 1)..].Trim(), out var index) ? index : 0;
    }

    public static string? ExtractExecutablePathFromCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        var expanded = Environment.ExpandEnvironmentVariables(command.Trim());
        var quoted = Regex.Match(expanded, "^\"(?<path>[^\"]+\\.(?:exe|com|bat|cmd))\"", RegexOptions.IgnoreCase);
        if (quoted.Success)
            return ResolveExecutablePath(quoted.Groups["path"].Value);

        var unquoted = Regex.Match(expanded, "(?<path>(?:[A-Za-z]:\\\\|%[^%]+%\\\\|\\\\\\\\)[^\\s\"']+\\.(?:exe|com|bat|cmd))", RegexOptions.IgnoreCase);
        if (unquoted.Success)
            return ResolveExecutablePath(unquoted.Groups["path"].Value);

        var firstToken = expanded.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return ResolveExecutablePath(firstToken);
    }

    private static string? ResolveExecutablePath(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        candidate = Environment.ExpandEnvironmentVariables(candidate.Trim().Trim('"'));
        if (Path.IsPathRooted(candidate) && File.Exists(candidate))
            return candidate;

        var names = Path.HasExtension(candidate)
            ? new[] { candidate }
            : new[] { candidate + ".exe", candidate + ".com", candidate + ".bat", candidate + ".cmd" };

        var searchDirs = new List<string>();
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(system)) searchDirs.Add(system);
        if (!string.IsNullOrWhiteSpace(windows)) searchDirs.Add(windows);

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
        searchDirs.AddRange(pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));

        foreach (var dir in searchDirs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var name in names)
            {
                try
                {
                    var path = Path.Combine(dir, name);
                    if (File.Exists(path))
                        return path;
                }
                catch
                {
                    // Ignore malformed PATH entries.
                }
            }
        }

        return null;
    }
}
