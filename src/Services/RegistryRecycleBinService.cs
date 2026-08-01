using System.Security.AccessControl;
using System.Text.Json;
using Microsoft.Win32;
using RightMgr.Models;

namespace RightMgr.Services;

public static class RegistryRecycleBinService
{
    private sealed class RecycleRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTimeOffset DeletedAt { get; set; } = DateTimeOffset.Now;
        public ContextMenuItemInfo Item { get; set; } = new();
        public RegistryNodeSnapshot? KeyTree { get; set; }
        public RegistryValueSnapshot? SingleValue { get; set; }
    }

    private sealed class RegistryNodeSnapshot
    {
        public string? SecurityDescriptor { get; set; }
        public List<RegistryValueSnapshot> Values { get; set; } = new();
        public Dictionary<string, RegistryNodeSnapshot> SubKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class RegistryValueSnapshot
    {
        public string Name { get; set; } = "";
        public RegistryValueKind Kind { get; set; }
        public string? StringValue { get; set; }
        public string[]? StringArrayValue { get; set; }
        public byte[]? BinaryValue { get; set; }
        public long? IntegerValue { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static string StorageDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RightMgr",
        "RecycleBin");

    public static IReadOnlyList<ContextMenuItemInfo> LoadItems()
    {
        if (!Directory.Exists(StorageDirectory))
            return [];

        var items = new List<ContextMenuItemInfo>();
        foreach (var path in Directory.EnumerateFiles(StorageDirectory, "*.json"))
        {
            try
            {
                var record = JsonSerializer.Deserialize<RecycleRecord>(File.ReadAllText(path), JsonOptions);
                if (record == null)
                    continue;

                var item = record.Item;
                item.BigCategory = "回收站";
                item.IsInRecycleBin = true;
                item.IsPendingDelete = false;
                item.RecycleBinId = record.Id;
                item.DeletedAt = record.DeletedAt;
                items.Add(item);
            }
            catch
            {
                // A damaged record must not prevent the rest of the recycle bin loading.
            }
        }

        return items.OrderByDescending(x => x.DeletedAt).ToList();
    }

    public static void BackupAndDelete(ContextMenuItemInfo item)
    {
        var record = CreateRecord(item);
        Directory.CreateDirectory(StorageDirectory);
        var recordPath = GetRecordPath(record.Id);
        WriteRecordAtomically(recordPath, record);

        ContextMenuRegistryEditor.Delete(item);
        if (ContextMenuRegistryEditor.Exists(item))
            throw new InvalidOperationException("删除后检测到注册表项仍然存在；安全备份已保留在回收站。");
    }

    public static void Restore(string recordId)
    {
        var path = GetRecordPath(recordId);
        if (!File.Exists(path))
            throw new InvalidOperationException("回收站记录不存在。");

        var record = JsonSerializer.Deserialize<RecycleRecord>(File.ReadAllText(path), JsonOptions)
                     ?? throw new InvalidOperationException("回收站记录损坏。");
        var (root, registryPath) = ResolveRootAndPath(record.Item);

        if (record.SingleValue != null)
        {
            using var key = root.OpenSubKey(registryPath, writable: true)
                            ?? throw new InvalidOperationException("原注册表父项不存在或没有写入权限。");
            if (key.GetValueNames().Contains(record.SingleValue.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!ValueMatches(key, record.SingleValue))
                    throw new InvalidOperationException("原位置已存在同名注册表值，且内容与备份不同，未覆盖现有数据。");
                File.Delete(path);
                return;
            }

            RestoreValue(key, record.SingleValue);
            if (!ValueMatches(key, record.SingleValue))
                throw new InvalidOperationException("恢复后的注册表值校验失败，本地记录已保留。");
        }
        else
        {
            using var existing = root.OpenSubKey(registryPath);
            if (existing != null)
            {
                if (!NodeMatches(existing, record.KeyTree ?? throw new InvalidOperationException("回收站记录缺少注册表快照。")))
                    throw new InvalidOperationException("原位置已存在同名注册表项，且内容与备份不同，未覆盖现有数据。");
                File.Delete(path);
                return;
            }

            using var key = root.CreateSubKey(registryPath, writable: true)
                            ?? throw new InvalidOperationException("无法创建原注册表项。");
            RestoreNode(key, record.KeyTree ?? throw new InvalidOperationException("回收站记录缺少注册表快照。"));

            using var restored = root.OpenSubKey(registryPath)
                                 ?? throw new InvalidOperationException("恢复后未找到注册表项，本地记录已保留。");
            if (!NodeMatches(restored, record.KeyTree))
                throw new InvalidOperationException("恢复后的注册表项校验失败，本地记录已保留。");
        }

        File.Delete(path);
    }

    private static RecycleRecord CreateRecord(ContextMenuItemInfo item)
    {
        var record = new RecycleRecord { Item = item };
        var (root, path) = ResolveRootAndPath(item);
        using var key = root.OpenSubKey(path, writable: false)
                        ?? throw new InvalidOperationException("注册表项不存在，无法备份。");

        if (ContextMenuRegistryEditor.IsValueOnlyItem(item))
        {
            var valueName = ContextMenuRegistryEditor.GetDeletedValueName(item);
            if (!key.GetValueNames().Contains(valueName, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("注册表值不存在，无法备份。");
            record.SingleValue = CaptureValue(key, valueName);
        }
        else
        {
            record.KeyTree = CaptureNode(key);
        }

        return record;
    }

    private static RegistryNodeSnapshot CaptureNode(RegistryKey key)
    {
        var node = new RegistryNodeSnapshot();
        try
        {
            node.SecurityDescriptor = key.GetAccessControl(AccessControlSections.Access)
                .GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        }
        catch
        {
            // ACL backup is best effort; values and subkeys are still recoverable.
        }

        foreach (var name in key.GetValueNames())
            node.Values.Add(CaptureValue(key, name));

        foreach (var name in key.GetSubKeyNames())
        {
            using var subKey = key.OpenSubKey(name, writable: false);
            if (subKey != null)
                node.SubKeys[name] = CaptureNode(subKey);
        }

        return node;
    }

    private static RegistryValueSnapshot CaptureValue(RegistryKey key, string name)
    {
        var kind = key.GetValueKind(name);
        var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
                    ?? (kind == RegistryValueKind.String || kind == RegistryValueKind.ExpandString ? "" : null);
        var snapshot = new RegistryValueSnapshot { Name = name, Kind = kind };
        switch (kind)
        {
            case RegistryValueKind.Binary:
            case RegistryValueKind.None:
                snapshot.BinaryValue = (byte[]?)value ?? [];
                break;
            case RegistryValueKind.MultiString:
                snapshot.StringArrayValue = (string[]?)value ?? [];
                break;
            case RegistryValueKind.DWord:
            case RegistryValueKind.QWord:
                snapshot.IntegerValue = Convert.ToInt64(value);
                break;
            default:
                snapshot.StringValue = value?.ToString() ?? "";
                break;
        }
        return snapshot;
    }

    private static void RestoreNode(RegistryKey key, RegistryNodeSnapshot node)
    {
        foreach (var value in node.Values)
            RestoreValue(key, value);

        foreach (var (name, child) in node.SubKeys)
        {
            using var subKey = key.CreateSubKey(name, writable: true)
                               ?? throw new InvalidOperationException($"无法恢复注册表子项 {name}。");
            RestoreNode(subKey, child);
        }

        if (!string.IsNullOrWhiteSpace(node.SecurityDescriptor))
        {
            var security = new RegistrySecurity();
            security.SetSecurityDescriptorSddlForm(node.SecurityDescriptor, AccessControlSections.Access);
            key.SetAccessControl(security);
        }
    }

    private static void RestoreValue(RegistryKey key, RegistryValueSnapshot value)
    {
        object data = value.Kind switch
        {
            RegistryValueKind.Binary or RegistryValueKind.None => value.BinaryValue ?? [],
            RegistryValueKind.MultiString => value.StringArrayValue ?? [],
            RegistryValueKind.DWord => Convert.ToInt32(value.IntegerValue),
            RegistryValueKind.QWord => value.IntegerValue ?? 0L,
            _ => value.StringValue ?? ""
        };
        key.SetValue(value.Name, data, value.Kind);
    }

    private static bool NodeMatches(RegistryKey key, RegistryNodeSnapshot node)
    {
        if (node.Values.Any(value => !ValueMatches(key, value)))
            return false;
        foreach (var (name, child) in node.SubKeys)
        {
            using var subKey = key.OpenSubKey(name);
            if (subKey == null || !NodeMatches(subKey, child))
                return false;
        }
        return true;
    }

    private static bool ValueMatches(RegistryKey key, RegistryValueSnapshot expected)
    {
        if (!key.GetValueNames().Contains(expected.Name, StringComparer.OrdinalIgnoreCase)
            || key.GetValueKind(expected.Name) != expected.Kind)
            return false;

        var actual = key.GetValue(expected.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return expected.Kind switch
        {
            RegistryValueKind.Binary or RegistryValueKind.None => actual is byte[] bytes && bytes.SequenceEqual(expected.BinaryValue ?? []),
            RegistryValueKind.MultiString => actual is string[] strings && strings.SequenceEqual(expected.StringArrayValue ?? []),
            RegistryValueKind.DWord or RegistryValueKind.QWord => Convert.ToInt64(actual) == expected.IntegerValue,
            _ => string.Equals(actual?.ToString() ?? "", expected.StringValue ?? "", StringComparison.Ordinal)
        };
    }

    private static void WriteRecordAtomically(string path, RecycleRecord record)
    {
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(record, JsonOptions));
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static string GetRecordPath(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Any(ch => !char.IsAsciiLetterOrDigit(ch)))
            throw new InvalidOperationException("回收站记录编号无效。");
        return Path.Combine(StorageDirectory, $"{id}.json");
    }

    private static (RegistryKey Root, string Path) ResolveRootAndPath(ContextMenuItemInfo item)
    {
        return item.SourceRoot switch
        {
            "HKCU" => (Registry.CurrentUser, item.RelativeRegistryPath),
            "HKLM" => (Registry.LocalMachine, item.RelativeRegistryPath),
            "HKCR" => (Registry.ClassesRoot, item.RelativeRegistryPath),
            _ => throw new InvalidOperationException("不支持的注册表根。")
        };
    }
}
