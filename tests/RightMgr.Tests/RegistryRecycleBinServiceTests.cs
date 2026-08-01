using Microsoft.Win32;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RightMgr.Models;
using RightMgr.Services;

namespace RightMgr.Tests;

[TestClass]
public sealed class RegistryRecycleBinServiceTests
{
    private string _testPath = "";

    [TestInitialize]
    public void Initialize()
    {
        _testPath = $@"Software\Classes\Directory\shell\RightMgrRecycleTest_{Guid.NewGuid():N}";
    }

    [TestCleanup]
    public void Cleanup()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_testPath, throwOnMissingSubKey: false);
    }

    [TestMethod]
    public void BackupDeleteAndRestorePreservesRegistryTreeAndRemovesRecord()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(_testPath, writable: true))
        {
            key.SetValue(null, "Test menu");
            key.SetValue("Number", 42, RegistryValueKind.DWord);
            key.SetValue("Paths", new[] { "one", "two" }, RegistryValueKind.MultiString);
            using var command = key.CreateSubKey("command", writable: true);
            command.SetValue(null, "test.exe \"%1\"");
        }

        var item = CreateItem();
        var beforeIds = RegistryRecycleBinService.LoadItems()
            .Select(x => x.RecycleBinId)
            .ToHashSet(StringComparer.Ordinal);

        RegistryRecycleBinService.BackupAndDelete(item);

        Assert.IsFalse(ContextMenuRegistryEditor.Exists(item));
        var recycled = RegistryRecycleBinService.LoadItems()
            .Single(x => x.RegistryPath == item.RegistryPath && !beforeIds.Contains(x.RecycleBinId));

        RegistryRecycleBinService.Restore(recycled.RecycleBinId!);

        using var restored = Registry.CurrentUser.OpenSubKey(_testPath);
        Assert.IsNotNull(restored);
        Assert.AreEqual("Test menu", restored.GetValue(null));
        Assert.AreEqual(42, restored.GetValue("Number"));
        CollectionAssert.AreEqual(new[] { "one", "two" }, (string[])restored.GetValue("Paths")!);
        using var commandKey = restored.OpenSubKey("command");
        Assert.AreEqual("test.exe \"%1\"", commandKey?.GetValue(null));
        Assert.IsFalse(RegistryRecycleBinService.LoadItems().Any(x => x.RecycleBinId == recycled.RecycleBinId));
    }

    [TestMethod]
    public void BackupDeleteAndRestorePreservesValueOnlyEntry()
    {
        const string valueName = "TestComponent";
        using (var key = Registry.CurrentUser.CreateSubKey(_testPath, writable: true))
            key.SetValue(valueName, "{11111111-2222-3333-4444-555555555555}", RegistryValueKind.String);

        var item = CreateItem();
        item.Kind = ContextMenuKind.ModernExtension;
        item.MiddleCategory = "PowerToys 组件";
        item.KeyName = valueName;
        item.Value = "{11111111-2222-3333-4444-555555555555}";

        RegistryRecycleBinService.BackupAndDelete(item);
        using (var deleted = Registry.CurrentUser.OpenSubKey(_testPath))
            Assert.IsFalse(deleted!.GetValueNames().Contains(valueName));

        var recycled = RegistryRecycleBinService.LoadItems()
            .Single(x => x.RegistryPath == item.RegistryPath && x.KeyName == valueName);
        RegistryRecycleBinService.Restore(recycled.RecycleBinId!);

        using var restored = Registry.CurrentUser.OpenSubKey(_testPath);
        Assert.AreEqual(item.Value, restored?.GetValue(valueName));
        Assert.IsFalse(RegistryRecycleBinService.LoadItems().Any(x => x.RecycleBinId == recycled.RecycleBinId));
    }

    private ContextMenuItemInfo CreateItem()
    {
        return new ContextMenuItemInfo
        {
            BigCategory = "文件夹背景",
            MiddleCategory = "shell 命令",
            Scope = "当前用户",
            AppliesTo = "文件夹背景",
            SourceRoot = "HKCU",
            RelativeRegistryPath = _testPath,
            RegistryPath = $@"HKEY_CURRENT_USER\{_testPath}",
            KeyName = _testPath[(_testPath.LastIndexOf('\\') + 1)..],
            MenuName = "Test menu",
            DisplayName = "Test menu",
            Kind = ContextMenuKind.ShellVerb
        };
    }
}
