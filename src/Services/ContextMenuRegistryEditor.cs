using Microsoft.Win32;
using RightMgr.Models;

namespace RightMgr.Services;

public static class ContextMenuRegistryEditor
{
    public static void Disable(ContextMenuItemInfo item)
    {
        using var key = OpenItemKey(item, writable: true) ?? throw new InvalidOperationException("注册表项不存在");

        if (item.Kind == ContextMenuKind.ShellVerb)
        {
            key.SetValue("LegacyDisable", "");
            return;
        }

        var current = key.GetValue(null)?.ToString();
        if (!string.IsNullOrWhiteSpace(current))
        {
            key.SetValue("_RightMgr_DisabledDefault", current);
            key.SetValue(null, "");
        }
    }

    public static void Enable(ContextMenuItemInfo item)
    {
        using var key = OpenItemKey(item, writable: true) ?? throw new InvalidOperationException("注册表项不存在");

        if (item.Kind == ContextMenuKind.ShellVerb)
        {
            key.DeleteValue("LegacyDisable", throwOnMissingValue: false);
            return;
        }

        var backup = key.GetValue("_RightMgr_DisabledDefault")?.ToString();
        if (!string.IsNullOrWhiteSpace(backup))
        {
            key.SetValue(null, backup);
            key.DeleteValue("_RightMgr_DisabledDefault", throwOnMissingValue: false);
        }
    }

    public static void Delete(ContextMenuItemInfo item)
    {
        var (root, path) = ResolveRootAndPath(item);
        var idx = path.LastIndexOf('\\');
        if (idx <= 0) throw new InvalidOperationException("注册表路径不合法");

        var parentPath = path[..idx];
        var keyName = path[(idx + 1)..];

        using var parent = root.OpenSubKey(parentPath, writable: true)
            ?? throw new InvalidOperationException("父级注册表项不存在");

        parent.DeleteSubKeyTree(keyName, throwOnMissingSubKey: false);
    }

    public static void SetIcon(ContextMenuItemInfo item, string iconResource)
    {
        if (string.IsNullOrWhiteSpace(iconResource))
            throw new InvalidOperationException("图标资源不能为空");

        if (item.Kind == ContextMenuKind.ShellVerb)
        {
            using var key = OpenItemKey(item, writable: true) ?? throw new InvalidOperationException("注册表项不存在");
            key.SetValue("Icon", iconResource.Trim());
            return;
        }

        if (string.IsNullOrWhiteSpace(item.Clsid))
            throw new InvalidOperationException("ShellEx 项没有 CLSID，无法定位 DefaultIcon");

        var (root, clsidPath) = ResolveClsidPath(item);
        using var iconKey = root.CreateSubKey($@"{clsidPath}\DefaultIcon", writable: true)
            ?? throw new InvalidOperationException("无法创建 CLSID DefaultIcon 项");

        iconKey.SetValue(null, iconResource.Trim());
    }

    public static void SetValue(ContextMenuItemInfo item, string value)
    {
        using var key = OpenItemKey(item, writable: true) ?? throw new InvalidOperationException("注册表项不存在");

        if (item.Kind == ContextMenuKind.ShellVerb)
        {
            using var command = key.OpenSubKey("command", writable: true);
            if (command != null)
            {
                command.SetValue(null, value);
                return;
            }
        }

        key.SetValue(null, value);
    }

    private static RegistryKey? OpenItemKey(ContextMenuItemInfo item, bool writable)
    {
        var (root, path) = ResolveRootAndPath(item);
        return root.OpenSubKey(path, writable);
    }

    private static (RegistryKey Root, string Path) ResolveClsidPath(ContextMenuItemInfo item)
    {
        var clsid = item.Clsid ?? throw new InvalidOperationException("CLSID 为空");

        return item.SourceRoot switch
        {
            "HKCU" => (Registry.CurrentUser, $@"Software\Classes\CLSID\{clsid}"),
            "HKLM" => (Registry.LocalMachine, $@"SOFTWARE\Classes\CLSID\{clsid}"),
            "HKCR" => (Registry.ClassesRoot, $@"CLSID\{clsid}"),
            _ => (Registry.ClassesRoot, $@"CLSID\{clsid}")
        };
    }

    private static (RegistryKey Root, string Path) ResolveRootAndPath(ContextMenuItemInfo item)
    {
        if (!string.IsNullOrWhiteSpace(item.SourceRoot) && !string.IsNullOrWhiteSpace(item.RelativeRegistryPath))
        {
            return item.SourceRoot switch
            {
                "HKCU" => (Registry.CurrentUser, item.RelativeRegistryPath),
                "HKLM" => (Registry.LocalMachine, item.RelativeRegistryPath),
                "HKCR" => (Registry.ClassesRoot, item.RelativeRegistryPath),
                _ => throw new InvalidOperationException("不支持的注册表根")
            };
        }

        var full = item.RegistryPath;
        if (full.StartsWith(@"HKEY_CURRENT_USER\", StringComparison.OrdinalIgnoreCase))
            return (Registry.CurrentUser, full[@"HKEY_CURRENT_USER\".Length..]);

        if (full.StartsWith(@"HKEY_LOCAL_MACHINE\", StringComparison.OrdinalIgnoreCase))
            return (Registry.LocalMachine, full[@"HKEY_LOCAL_MACHINE\".Length..]);

        if (full.StartsWith(@"HKEY_CLASSES_ROOT\", StringComparison.OrdinalIgnoreCase))
            return (Registry.ClassesRoot, full[@"HKEY_CLASSES_ROOT\".Length..]);

        throw new InvalidOperationException("不支持的注册表路径");
    }
}
