using System.Xml.Linq;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The item function's notifyDlg / notifyMsg (function.xml of CSceneStateGame::OnItemUse, scenestategame.cpp:8626).</summary>
public class ItemFunctionNotifyTests
{
    static ItemData Item(string parameter) =>
        ItemData.Load(1, new XElement("e", new XElement("function", new XAttribute("name", "QuestScroll"), new XAttribute("parameter", parameter))));

    [Test]
    public void XmlParameterCarriesTheNotifyAttributes()
    {
        var m = Item("<v questID='80000227' notifyMsg=\"s_quest_scroll_quest\"/>");
        Assert.That((m.NotifyMsg, m.NotifyDlg), Is.EqualTo(("s_quest_scroll_quest", "")));
        var d = Item("<v questID='89000211' notifyDlg=\"TimeRunEventDialog\"/>");
        Assert.That((d.NotifyMsg, d.NotifyDlg), Is.EqualTo(("", "TimeRunEventDialog")));
    }

    [TestCase("")] [TestCase("1,2,3")] [TestCase("<v questID='1'/>")] [TestCase("<broken")]
    public void OtherParametersHaveNoNotify(string parameter)
    {
        var i = Item(parameter);
        Assert.That((i.NotifyMsg, i.NotifyDlg), Is.EqualTo(("", "")));
    }
}
