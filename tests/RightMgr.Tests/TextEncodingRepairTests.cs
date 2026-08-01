using Microsoft.VisualStudio.TestTools.UnitTesting;
using RightMgr.Services;

namespace RightMgr.Tests;

[TestClass]
public sealed class TextEncodingRepairTests
{
    [TestMethod]
    public void RepairsUtf8TextMisreadAsGbk()
    {
        const string mojibake = "浣跨敤璁颁簨鏈墦寮€锛圧ightMgr 鍥炴敹绔欐祴璇曪級";
        Assert.AreEqual("使用记事本打开（RightMgr 回收站测试）", TextEncodingRepair.RepairMojibake(mojibake));
    }

    [TestMethod]
    public void KeepsNormalChineseTextUnchanged()
    {
        const string normal = "使用记事本打开（RightMgr 回收站测试）";
        Assert.AreEqual(normal, TextEncodingRepair.RepairMojibake(normal));
    }
}
