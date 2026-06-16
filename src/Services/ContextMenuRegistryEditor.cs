using Microsoft.Win32;
using RightMgr.Models;

namespace RightMgr.Services;

public static class ContextMenuRegistryEditor
{
    public static bool CanDelete(ContextMenuItemInfo item, out string? error)
    {
        error = null;

        try
        {
            if (IsPowerToysComponent(item))
            {
                using var key = OpenItemKey(item, writable: true);
                if (key == null)
                {
                    error = "注册表项不存在";
                    return false;
                }

                return true;
            }

            var (root, path) = ResolveRootAndPath(item);
            var idx = path.LastIndexOf('\\');
            if (idx <= 0)
            {
                error = "注册表路径不合法";
                return false;
            }

            var parentPath = path[..idx];
            var keyName = path[(idx + 1)..];

            using var parent = root.OpenSubKey(parentPath, writable: true);
            if (parent == null)
            {
                error = "父级注册表项不存在或没有写入权限";
                return false;
            }

            using var target = parent.OpenSubKey(keyName, writable: true);
            if (target == null)
            {
                error = "注册表项不存在或没有写入权限";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static void Disable(ContextMenuItemInfo item)
    {
        using var key = OpenItemKey(item, writable: true) ?? throw new InvalidOperationException("注册表项不存在");

        if (item.Kind == ContextMenuKind.ShellVerb)
        {
            key.SetValue("LegacyDisable", "");
            return;
        }

        if (IsPowerToysComponent(item))
        {
            var currentComponentValue = key.GetValue(item.KeyName)?.ToString();
            if (currentComponentValue != null)
            {
                key.SetValue(GetDisabledValueName(item.KeyName), currentComponentValue);
                key.DeleteValue(item.KeyName, throwOnMissingValue: false);
            }
            return;
        }

        if (item.Kind == ContextMenuKind.ModernExtension)
        {
            if (IsContextMenuOptInClsid(item))
            {
                key.SetValue("_RightMgr_Disabled_ContextMenuOptIn", key.GetValue("ContextMenuOptIn")?.ToString() ?? "");
                key.DeleteValue("ContextMenuOptIn", throwOnMissingValue: false);
                return;
            }

            key.SetValue("_RightMgr_Disabled", "1");
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

        if (IsPowerToysComponent(item))
        {
            var disabledName = GetDisabledValueName(item.KeyName);
            var componentBackup = key.GetValue(disabledName)?.ToString();
            if (componentBackup != null)
            {
                key.SetValue(item.KeyName, componentBackup);
                key.DeleteValue(disabledName, throwOnMissingValue: false);
            }
            return;
        }

        if (item.Kind == ContextMenuKind.ModernExtension)
        {
            if (IsContextMenuOptInClsid(item))
            {
                key.SetValue("ContextMenuOptIn", key.GetValue("_RightMgr_Disabled_ContextMenuOptIn")?.ToString() ?? "");
                key.DeleteValue("_RightMgr_Disabled_ContextMenuOptIn", throwOnMissingValue: false);
                return;
            }

            key.DeleteValue("_RightMgr_Disabled", throwOnMissingValue: false);
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
        if (IsPowerToysComponent(item))
        {
            using var key = OpenItemKey(item, writable: true) ?? throw new InvalidOperationException("注册表项不存在");
            key.DeleteValue(item.IsEnabled ? item.KeyName : GetDisabledValueName(item.KeyName), throwOnMissingValue: false);
            return;
        }
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

        if (item.Kind == ContextMenuKind.ModernExtension)
        {
            if (IsPowerToysComponent(item))
            {
                var (powerToysRoot, path) = ResolveRootAndPath(item);
                var componentsSuffix = @"\components";
                var powerToysPath = path.EndsWith(componentsSuffix, StringComparison.OrdinalIgnoreCase)
                    ? path[..^componentsSuffix.Length]
                    : path;
                using var powerToysIconKey = powerToysRoot.CreateSubKey($@"{powerToysPath}\DefaultIcon", writable: true)
                    ?? throw new InvalidOperationException("无法创建 PowerToys DefaultIcon 项");
                powerToysIconKey.SetValue(null, iconResource.Trim());
                return;
            }

            using var key = OpenItemKey(item, writable: true) ?? throw new InvalidOperationException("注册表项不存在");
            key.SetValue("DllPath", iconResource.Trim());
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

        if (IsPowerToysComponent(item))
        {
            key.SetValue(item.IsEnabled ? item.KeyName : GetDisabledValueName(item.KeyName), value);
            return;
        }

        if (item.Kind == ContextMenuKind.ShellVerb)
        {
            using var command = key.OpenSubKey("command", writable: true);
            if (command != null)
            {
                command.SetValue(null, value);
                return;
            }
        }

        if (item.Kind == ContextMenuKind.ModernExtension)
        {
            if (key.GetValue("ApplicationDisplayName") != null)
                key.SetValue("ApplicationDisplayName", value);
            else if (key.GetValue("DisplayName") != null)
                key.SetValue("DisplayName", value);
            else
                key.SetValue(null, value);
            return;
        }

        key.SetValue(null, value);
    }

    private static RegistryKey? OpenItemKey(ContextMenuItemInfo item, bool writable)
    {
        var (root, path) = ResolveRootAndPath(item);
        return root.OpenSubKey(path, writable);
    }

    private static bool IsPowerToysComponent(ContextMenuItemInfo item)
    {
        return item.Kind == ContextMenuKind.ModernExtension
               && item.MiddleCategory.Equals("PowerToys 组件", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsContextMenuOptInClsid(ContextMenuItemInfo item)
    {
        return item.Kind == ContextMenuKind.ModernExtension
               && item.MiddleCategory.Equals("COM ContextMenuOptIn", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDisabledValueName(string valueName) => $"_RightMgr_Disabled_{valueName}";

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
