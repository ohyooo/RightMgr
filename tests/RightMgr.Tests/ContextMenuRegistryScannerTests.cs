using Microsoft.Win32;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RightMgr.Models;
using RightMgr.Services;

namespace RightMgr.Tests;

[TestClass]
public sealed class ContextMenuRegistryScannerTests
{
    private const string PowerToysPath = @"Software\Classes\powertoys";
    private const string PackagedComPath = @"Software\Classes\PackagedCom";
    private const string TestShellPath = @"Software\Classes\Directory\shell\RightMgrTestDelete";
    private const string TestPowerRenameClsidPath = @"Software\Classes\CLSID\{11111111-1111-1111-1111-111111111111}";
    private const string TestFileLocksmithClsidPath = @"Software\Classes\CLSID\{22222222-2222-2222-2222-222222222222}";

    [TestCleanup]
    public void Cleanup()
    {
        Registry.CurrentUser.DeleteSubKeyTree(PowerToysPath, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(PackagedComPath, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(TestShellPath, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(TestPowerRenameClsidPath, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(TestFileLocksmithClsidPath, throwOnMissingSubKey: false);
    }

    [TestMethod]
    public void ScanAllIncludesContextMenuOptInClsids()
    {
        const string clsid = "{11111111-1111-1111-1111-111111111111}";
        using var clsidKey = Registry.CurrentUser.CreateSubKey(TestPowerRenameClsidPath, writable: true);
        Assert.IsNotNull(clsidKey);
        clsidKey.SetValue(null, "PowerRename Shell Extension");
        clsidKey.SetValue("ContextMenuOptIn", "");
        using var inproc = clsidKey.CreateSubKey("InprocServer32", writable: true);
        Assert.IsNotNull(inproc);
        inproc.SetValue(null, @"C:\PowerToys.PowerRenameExt.dll");

        var items = ContextMenuRegistryScanner.ScanAll();

        var powerRename = items.SingleOrDefault(x => x.SourceRoot == "HKCU" && x.Clsid == clsid);

        Assert.IsNotNull(powerRename);
        Assert.AreEqual(ContextMenuKind.ModernExtension, powerRename.Kind);
        Assert.AreEqual("COM ContextMenuOptIn", powerRename.MiddleCategory);
        Assert.AreEqual("PowerRename Shell Extension", powerRename.DisplayName);
        Assert.IsTrue(powerRename.IsEnabled);
        Assert.IsFalse(powerRename.IsReadOnly);
    }

    [TestMethod]
    public void ScanAllIncludesPackagedComServers()
    {
        const string packageName = "Contoso.RoboCopyEx_1.0.0.0_x64__test";
        const string clsid = "{3282E233-C5D3-4533-9B25-44B8AAAFACFA}";

        using var server = Registry.CurrentUser.CreateSubKey($@"{PackagedComPath}\Package\{packageName}\Server\0", writable: true);
        Assert.IsNotNull(server);
        server.SetValue("ApplicationDisplayName", "RoboCopyEx");
        server.SetValue("DllPath", @"C:\Program Files\WindowsApps\Contoso.RoboCopyEx\RoboCopyEx.dll");

        using var classPackage = Registry.CurrentUser.CreateSubKey($@"{PackagedComPath}\ClassIndex\{clsid}\{packageName}", writable: true);
        Assert.IsNotNull(classPackage);

        var items = ContextMenuRegistryScanner.ScanAll();

        var item = items.SingleOrDefault(x => x.SourceRoot == "HKCU" && x.MenuName == packageName && x.DisplayName == "RoboCopyEx");

        Assert.IsNotNull(item);
        Assert.AreEqual(ContextMenuKind.ModernExtension, item.Kind);
        Assert.AreEqual("Packaged COM / AppX", item.MiddleCategory);
        Assert.AreEqual(clsid, item.Clsid);
        Assert.IsFalse(item.IsReadOnly);
        Assert.AreEqual(@"C:\Program Files\WindowsApps\Contoso.RoboCopyEx\RoboCopyEx.dll", item.InProcServer32);
    }

    [TestMethod]
    public void EditorCanDisableEnableAndDeleteContextMenuOptInClsid()
    {
        const string clsid = "{22222222-2222-2222-2222-222222222222}";
        using var clsidKey = Registry.CurrentUser.CreateSubKey(TestFileLocksmithClsidPath, writable: true);
        Assert.IsNotNull(clsidKey);
        clsidKey.SetValue(null, "File Locksmith Shell Extension");
        clsidKey.SetValue("ContextMenuOptIn", "");
        using var inproc = clsidKey.CreateSubKey("InprocServer32", writable: true);
        Assert.IsNotNull(inproc);
        inproc.SetValue(null, @"C:\PowerToys.FileLocksmithExt.dll");

        var item = ContextMenuRegistryScanner.ScanAll()
            .Single(x => x.SourceRoot == "HKCU" && x.Clsid == clsid);

        ContextMenuRegistryEditor.Disable(item);
        Assert.IsNull(clsidKey.GetValue("ContextMenuOptIn"));
        Assert.IsNotNull(clsidKey.GetValue("_RightMgr_Disabled_ContextMenuOptIn"));

        var disabledItem = ContextMenuRegistryScanner.ScanAll()
            .Single(x => x.SourceRoot == "HKCU" && x.Clsid == clsid);
        Assert.IsFalse(disabledItem.IsEnabled);

        ContextMenuRegistryEditor.Enable(disabledItem);
        Assert.IsNotNull(clsidKey.GetValue("ContextMenuOptIn"));
        Assert.IsNull(clsidKey.GetValue("_RightMgr_Disabled_ContextMenuOptIn"));

        var enabledItem = ContextMenuRegistryScanner.ScanAll()
            .Single(x => x.SourceRoot == "HKCU" && x.Clsid == clsid);
        ContextMenuRegistryEditor.Delete(enabledItem);
        using var deleted = Registry.CurrentUser.OpenSubKey(TestFileLocksmithClsidPath);
        Assert.IsNull(deleted);
    }

    [TestMethod]
    public void CanDeleteChecksWritableTargetWithoutDeleting()
    {
        using var key = Registry.CurrentUser.CreateSubKey(TestShellPath, writable: true);
        Assert.IsNotNull(key);
        key.SetValue(null, "RightMgr test");

        var item = new ContextMenuItemInfo
        {
            SourceRoot = "HKCU",
            RelativeRegistryPath = TestShellPath,
            RegistryPath = $@"HKEY_CURRENT_USER\{TestShellPath}",
            KeyName = "RightMgrTestDelete",
            Kind = ContextMenuKind.ShellVerb
        };

        var canDelete = ContextMenuRegistryEditor.CanDelete(item, out var error);

        Assert.IsTrue(canDelete, error);
        using var stillThere = Registry.CurrentUser.OpenSubKey(TestShellPath);
        Assert.IsNotNull(stillThere);
    }
}
