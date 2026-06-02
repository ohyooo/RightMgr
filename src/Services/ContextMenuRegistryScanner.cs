using Microsoft.Win32;
using RightMgr.Models;

namespace RightMgr.Services;

public static class ContextMenuRegistryScanner
{
    private sealed record RegistryRoot(RegistryKey Key, string Label, string DisplayPrefix, string ClassesPrefix, string Scope);
    private sealed record ScanLocation(string Path, string AppliesTo, ContextMenuKind Kind);

    private static readonly RegistryRoot[] Roots =
    {
        new(Registry.CurrentUser, "HKCU", "HKEY_CURRENT_USER", @"Software\Classes", "当前用户"),
        new(Registry.LocalMachine, "HKLM", "HKEY_LOCAL_MACHINE", @"SOFTWARE\Classes", "所有用户"),
        new(Registry.ClassesRoot, "HKCR", "HKEY_CLASSES_ROOT", "", "合并视图")
    };

    private static readonly ScanLocation[] BaseLocations =
    {
        new(@"*\shell", "所有文件", ContextMenuKind.ShellVerb),
        new(@"*\shellex\ContextMenuHandlers", "所有文件", ContextMenuKind.ShellExHandler),
        new(@"AllFileSystemObjects\shell", "文件和文件夹", ContextMenuKind.ShellVerb),
        new(@"AllFileSystemObjects\shellex\ContextMenuHandlers", "文件和文件夹", ContextMenuKind.ShellExHandler),
        new(@"Directory\shell", "目录", ContextMenuKind.ShellVerb),
        new(@"Directory\shellex\ContextMenuHandlers", "目录", ContextMenuKind.ShellExHandler),
        new(@"Directory\Background\shell", "目录空白处", ContextMenuKind.ShellVerb),
        new(@"Directory\Background\shellex\ContextMenuHandlers", "目录空白处", ContextMenuKind.ShellExHandler),
        new(@"Folder\shell", "文件夹", ContextMenuKind.ShellVerb),
        new(@"Folder\shellex\ContextMenuHandlers", "文件夹", ContextMenuKind.ShellExHandler),
        new(@"Drive\shell", "磁盘", ContextMenuKind.ShellVerb),
        new(@"Drive\shellex\ContextMenuHandlers", "磁盘", ContextMenuKind.ShellExHandler),
        new(@"DesktopBackground\Shell", "桌面空白处", ContextMenuKind.ShellVerb),
        new(@"DesktopBackground\shellex\ContextMenuHandlers", "桌面空白处", ContextMenuKind.ShellExHandler),
        new(@"LibraryFolder\shell", "库", ContextMenuKind.ShellVerb),
        new(@"LibraryFolder\shellex\ContextMenuHandlers", "库", ContextMenuKind.ShellExHandler),
        new(@"SystemFileAssociations\*\shell", "系统文件关联", ContextMenuKind.ShellVerb),
        new(@"SystemFileAssociations\*\shellex\ContextMenuHandlers", "系统文件关联", ContextMenuKind.ShellExHandler)
    };

    private static readonly HashSet<string> CommonExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".md", ".json", ".xml", ".ini", ".cfg", ".conf", ".csv",
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".ico",
        ".mp3", ".wav", ".flac", ".mp4", ".mkv", ".avi", ".mov",
        ".zip", ".rar", ".7z", ".tar", ".gz",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".exe", ".msi", ".bat", ".cmd", ".ps1", ".lnk",
        ".cs", ".js", ".ts", ".html", ".css", ".py", ".java", ".cpp", ".h"
    };

    public static List<ContextMenuItemInfo> ScanAll()
    {
        var result = new List<ContextMenuItemInfo>();

        foreach (var root in Roots)
        {
            foreach (var location in BaseLocations)
                ScanPath(result, root, location);

            ScanFileExtensions(result, root);
        }

        return result
            .GroupBy(GetDedupeKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(x => GetRootPriority(x.SourceRoot)).First())
            .OrderBy(x => x.BigCategory)
            .ThenBy(x => x.AppliesTo)
            .ThenBy(x => x.MiddleCategory)
            .ThenBy(x => x.DisplayName)
            .ToList();
    }

    public static string FormatAll(IEnumerable<ContextMenuItemInfo> items)
    {
        return string.Join(Environment.NewLine, items.Select((item, index) =>
            $"[{index + 1}] {item.DisplayName}\n" +
            $"  分类: {item.BigCategory} / {item.AppliesTo} / {item.MiddleCategory}\n" +
            $"  范围: {item.Scope} ({item.SourceRoot})\n" +
            $"  注册表: {item.RegistryPath}\n" +
            $"  项: {item.KeyName}\n" +
            $"  值名: {item.ValueName}\n" +
            $"  值: {item.Value}\n" +
            $"  CLSID: {item.Clsid}\n" +
            $"  DLL: {item.InProcServer32}\n" +
            $"  启用: {item.IsEnabled}\n"));
    }

    private static void ScanPath(List<ContextMenuItemInfo> result, RegistryRoot root, ScanLocation location)
    {
        var relativePath = Combine(root.ClassesPrefix, location.Path);
        using var key = root.Key.OpenSubKey(relativePath);
        if (key == null) return;

        foreach (var subName in key.GetSubKeyNames())
        {
            using var sub = key.OpenSubKey(subName);
            if (sub == null) continue;

            AddItem(result, root, location, sub, subName);
        }
    }

    private static void ScanFileExtensions(List<ContextMenuItemInfo> result, RegistryRoot root)
    {
        using var classes = root.Key.OpenSubKey(root.ClassesPrefix);
        if (classes == null) return;

        foreach (var ext in classes.GetSubKeyNames().Where(x => x.StartsWith(".", StringComparison.Ordinal)))
        {
            if (!ShouldScanExtension(root, classes, ext))
                continue;

            var appliesTo = $"扩展名 {ext}";
            var scannedAssociations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            ScanAssociationPaths(result, root, ext, appliesTo, scannedAssociations);
            ScanPath(result, root, new ScanLocation($@"SystemFileAssociations\{ext}\shell", appliesTo, ContextMenuKind.ShellVerb));
            ScanPath(result, root, new ScanLocation($@"SystemFileAssociations\{ext}\shellex\ContextMenuHandlers", appliesTo, ContextMenuKind.ShellExHandler));

            using var extKey = classes.OpenSubKey(ext);
            if (extKey == null) continue;

            var progId = extKey.GetValue(null)?.ToString();
            ScanAssociationPaths(result, root, progId, appliesTo, scannedAssociations);

            using var openWithProgIds = extKey.OpenSubKey("OpenWithProgids");
            foreach (var openWithProgId in openWithProgIds?.GetValueNames() ?? Array.Empty<string>())
                ScanAssociationPaths(result, root, openWithProgId, appliesTo, scannedAssociations);

            var perceivedType = extKey.GetValue("PerceivedType")?.ToString();
            if (!string.IsNullOrWhiteSpace(perceivedType))
            {
                ScanPath(result, root, new ScanLocation($@"SystemFileAssociations\{perceivedType}\shell", appliesTo, ContextMenuKind.ShellVerb));
                ScanPath(result, root, new ScanLocation($@"SystemFileAssociations\{perceivedType}\shellex\ContextMenuHandlers", appliesTo, ContextMenuKind.ShellExHandler));
            }
        }
    }

    private static bool ShouldScanExtension(RegistryRoot root, RegistryKey classes, string ext)
    {
        if (CommonExtensions.Contains(ext))
            return true;

        if (root.Label == "HKCU")
            return HasExtensionMenu(classes, ext);

        return false;
    }

    private static bool HasExtensionMenu(RegistryKey classes, string ext)
    {
        using var shell = classes.OpenSubKey($@"{ext}\shell");
        if (shell?.GetSubKeyNames().Length > 0)
            return true;

        using var shellex = classes.OpenSubKey($@"{ext}\shellex\ContextMenuHandlers");
        return shellex?.GetSubKeyNames().Length > 0;
    }

    private static void ScanAssociationPaths(
        List<ContextMenuItemInfo> result,
        RegistryRoot root,
        string? association,
        string appliesTo,
        HashSet<string> scannedAssociations)
    {
        if (string.IsNullOrWhiteSpace(association) || !scannedAssociations.Add(association))
            return;

        ScanPath(result, root, new ScanLocation($@"{association}\shell", appliesTo, ContextMenuKind.ShellVerb));
        ScanPath(result, root, new ScanLocation($@"{association}\shellex\ContextMenuHandlers", appliesTo, ContextMenuKind.ShellExHandler));
    }

    private static void AddItem(List<ContextMenuItemInfo> result, RegistryRoot root, ScanLocation location, RegistryKey sub, string subName)
    {
        var defaultValue = sub.GetValue(null)?.ToString();
        var muiVerb = sub.GetValue("MUIVerb")?.ToString();
        var command = GetCommand(sub);
        var rawName = FirstNonEmpty(muiVerb, defaultValue, subName) ?? subName;

        var clsid = location.Kind == ContextMenuKind.ShellVerb ? null : ShellResourceResolver.ExtractClsid(defaultValue);
        var clsidName = clsid == null ? null : ShellResourceResolver.ResolveClsidName(clsid);
        var localized = clsid == null ? null : ShellResourceResolver.ResolveClsidLocalizedString(clsid);
        var inproc = clsid == null ? null : ShellResourceResolver.ResolveInProcServer32(clsid);
        var iconResource = GetShellIcon(sub) ?? (clsid == null ? null : ShellResourceResolver.ResolveClsidIcon(clsid)) ?? inproc;
        var commandExecutable = ShellResourceResolver.ExtractExecutablePathFromCommand(command);
        var iconFile = ShellIconService.GetPngIconPathForTarget(location.AppliesTo, iconResource ?? commandExecutable);

        var displayName = FirstNonEmpty(
            ShellResourceResolver.ResolveDisplayName(muiVerb),
            ShellResourceResolver.ResolveClsidDisplayName(muiVerb),
            clsidName,
            ShellResourceResolver.ResolveDisplayName(defaultValue),
            ShellResourceResolver.ResolveClsidDisplayName(defaultValue),
            ShellResourceResolver.ResolveClsidDisplayName(subName),
            HumanizeKeyName(subName));
        var resolvedDisplayName = string.IsNullOrWhiteSpace(displayName) ? subName : displayName;

        var relativeItemPath = Combine(root.ClassesPrefix, location.Path, subName);

        result.Add(new ContextMenuItemInfo
        {
            BigCategory = GetBigCategory(location.AppliesTo),
            MiddleCategory = location.Kind == ContextMenuKind.ShellVerb ? "传统 shell 命令" : "ShellEx/COM 扩展",
            SmallCategory = GetSmallCategory(location.Path),
            Scope = root.Scope,
            AppliesTo = location.AppliesTo,
            SourceRoot = root.Label,
            RelativeRegistryPath = relativeItemPath,
            MenuName = rawName,
            DisplayName = resolvedDisplayName,
            RegistryPath = $@"{root.DisplayPrefix}\{relativeItemPath}",
            KeyName = subName,
            ValueName = location.Kind == ContextMenuKind.ShellVerb ? "MUIVerb / 默认值 / command" : "默认值 CLSID",
            Value = location.Kind == ContextMenuKind.ShellVerb ? FirstNonEmpty(command, defaultValue) : defaultValue,
            Kind = location.Kind,
            IsEnabled = IsEnabled(sub, location.Kind),
            Clsid = clsid,
            ClsidName = clsidName,
            LocalizedString = localized,
            IconResource = iconResource,
            IconFilePath = iconFile,
            InProcServer32 = inproc,
            Description = BuildDescription(location, clsidName, resolvedDisplayName, inproc)
        });
    }

    private static string? GetCommand(RegistryKey key)
    {
        using var cmd = key.OpenSubKey("command");
        return cmd?.GetValue(null)?.ToString();
    }

    private static string? GetShellIcon(RegistryKey key) => key.GetValue("Icon")?.ToString();

    private static bool IsEnabled(RegistryKey key, ContextMenuKind kind)
    {
        if (kind == ContextMenuKind.ShellVerb)
            return key.GetValue("LegacyDisable") == null && key.GetValue("ProgrammaticAccessOnly") == null;

        return string.IsNullOrEmpty(key.GetValue("_RightMgr_DisabledDefault")?.ToString());
    }

    private static string GetBigCategory(string appliesTo)
    {
        if (appliesTo.StartsWith("扩展名", StringComparison.Ordinal)) return "按扩展名";
        if (appliesTo.Contains("空白处", StringComparison.Ordinal)) return "空白处菜单";
        if (appliesTo.Contains("磁盘", StringComparison.Ordinal)) return "磁盘菜单";
        if (appliesTo.Contains("文件", StringComparison.Ordinal) && appliesTo.Contains("文件夹", StringComparison.Ordinal)) return "通用对象";
        if (appliesTo.Contains("文件", StringComparison.Ordinal)) return "文件菜单";
        if (appliesTo.Contains("目录", StringComparison.Ordinal) || appliesTo.Contains("文件夹", StringComparison.Ordinal)) return "文件夹菜单";
        return "其它";
    }

    private static string GetSmallCategory(string path)
    {
        if (path.StartsWith(".", StringComparison.Ordinal))
            return path.Split('\\')[0];

        return path
            .Replace(@"\shellex\ContextMenuHandlers", "", StringComparison.OrdinalIgnoreCase)
            .Replace(@"\shell", "", StringComparison.OrdinalIgnoreCase)
            .Replace(@"\Shell", "", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildDescription(ScanLocation location, string? clsidName, string rawName, string? inproc)
    {
        var source = location.Kind == ContextMenuKind.ShellVerb ? "传统命令" : "COM 扩展";
        var name = FirstNonEmpty(clsidName, rawName) ?? "未命名";
        var dll = string.IsNullOrWhiteSpace(inproc) ? "" : $" · {inproc}";
        return $"{source} · 作用于 {location.AppliesTo} · {name}{dll}";
    }

    private static string GetDedupeKey(ContextMenuItemInfo item)
    {
        var owner = string.IsNullOrWhiteSpace(item.Clsid) ? item.KeyName : item.Clsid;
        return $"{NormalizeClassesPath(item.RelativeRegistryPath)}|{owner}";
    }

    private static string NormalizeClassesPath(string path)
    {
        return path
            .Replace(@"Software\Classes\", "", StringComparison.OrdinalIgnoreCase)
            .Replace(@"SOFTWARE\Classes\", "", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetRootPriority(string sourceRoot) => sourceRoot switch
    {
        "HKCU" => 0,
        "HKLM" => 1,
        _ => 2
    };

    private static string HumanizeKeyName(string value)
    {
        return value.EndsWith("Ext", StringComparison.OrdinalIgnoreCase) ? value[..^3] : value;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
    }

    private static string Combine(params string[] parts)
    {
        return string.Join('\\', parts.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim('\\')));
    }
}


