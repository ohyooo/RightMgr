using Microsoft.VisualStudio.TestTools.UnitTesting;
using RightMgr.Models;
using RightMgr.Services;

namespace RightMgr.Tests;

[TestClass]
public sealed class ElevatedDeleteServiceTests
{
    [TestMethod]
    public void EncodeItemRoundTripsRegistryIdentity()
    {
        var item = new ContextMenuItemInfo
        {
            SourceRoot = "HKLM",
            RelativeRegistryPath = @"SOFTWARE\Classes\Directory\shell\Example",
            RegistryPath = @"HKEY_LOCAL_MACHINE\SOFTWARE\Classes\Directory\shell\Example",
            KeyName = "Example",
            DisplayName = "Example",
            Kind = ContextMenuKind.ShellVerb,
            IsEnabled = true
        };

        var payload = ElevatedDeleteService.EncodeItem(item);
        var decoded = ElevatedDeleteService.DecodeItem(payload);

        Assert.IsNotNull(decoded);
        Assert.AreEqual(item.SourceRoot, decoded.SourceRoot);
        Assert.AreEqual(item.RelativeRegistryPath, decoded.RelativeRegistryPath);
        Assert.AreEqual(item.RegistryPath, decoded.RegistryPath);
        Assert.AreEqual(item.Kind, decoded.Kind);
    }

    [TestMethod]
    public void IsPermissionFailureRecognizesUnauthorizedAccess()
    {
        Assert.IsTrue(ElevatedDeleteService.IsPermissionFailure(new UnauthorizedAccessException()));
        Assert.IsFalse(ElevatedDeleteService.IsPermissionFailure(new InvalidOperationException()));
    }
}
