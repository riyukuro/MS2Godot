using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The talk's beauty open-dialog actions (uitalkcinematic.cpp:1190-1196, 1327-1400, 1981-2020).</summary>
public class BeautyTalkCommandTests
{
    sealed class Cust : IBeautyCustomize
    {
        public int EditItemId { get; set; }
        public bool HasChanges { get; set; }
        public bool HasStyleChanges { get; set; }
        public bool IsEditItemAdded { get; set; }
        public bool IsEditItemColorChanged { get; set; }
        public bool IsDragging { get; set; }
        public void UpdateEditItems() { }
        public void CommitSlot(int s) { }
        public void RestoreSlot(int s) { }
    }

    sealed class Rig
    {
        public int Npc = 77;
        public int ShopShown, StyleShown, Ended;
        public List<BeautyRequest> Sent = new();
        public BeautyTalkCommand Cmd;
        public Rig(bool customize = true)
        {
            Cmd = new BeautyTalkCommand
            {
                Shop = new BeautyShopFlow { CreateCustomize = () => customize ? new Cust() : null, CreateController = () => new BeautyShopDialogController(), Show = _ => ShopShown++ },
                Style = new BeautyStyleFlow { CreateCustomize = () => customize ? new Cust() : null, CreateController = () => new BeautyStyleDialogController(), Show = _ => StyleShown++ },
                TalkNpcId = () => Npc,
                Send = Sent.Add,
                EndTalk = () => Ended++,
            };
        }
    }

    [Test]
    public void TheStyleActionOpensTheStyleDialogWithEnterModeFour()
    {
        var r = new Rig();
        Assert.That(r.Cmd.Run("BeautyStyleDialog", "anything"), Is.True);
        Assert.That(r.StyleShown, Is.EqualTo(1));
        Assert.That(r.ShopShown, Is.EqualTo(0));
        Assert.That(r.Sent, Is.EqualTo(new[] { new BeautyEnterRequest(77, 4) }));
        Assert.That(r.Ended, Is.EqualTo(1));
    }

    [Test]
    public void TheShopActionPicksTheDialogAndModeFromItsArgument()
    {
        var r = new Rig();
        Assert.That(r.Cmd.Run("beautyshopdialog", "hair"), Is.True);
        Assert.That(r.ShopShown, Is.EqualTo(1));
        Assert.That(r.Sent, Is.EqualTo(new[] { new BeautyEnterRequest(77, 1) }));
    }

    [Test]
    public void TheStyleSaveKindOpensTheStyleDialog()
    {
        var r = new Rig();
        r.Cmd.Run("BeautyShopDialog", "face,styleSave");
        Assert.That(r.StyleShown, Is.EqualTo(1));
        Assert.That(r.Sent, Is.EqualTo(new[] { new BeautyEnterRequest(77, 4) }));
    }

    [TestCase("hair", "", 0x4eee, 1)]
    [TestCase("skin", "", 0x4eee, 3)]
    [TestCase("skin", "random", 0x4eee, 2)]
    [TestCase("makeUp", "random", 0x4eee, 2)]
    [TestCase("makeUp", "styleSave", 0x4f03, 4)]
    [TestCase("itemColor", "", 0x4e23, 3)]
    [TestCase("mirror", "", 0x4e24, 3)]
    [TestCase("other", "", 0, 0)]
    public void KindAndModeGiveTheDialogIdAndType(string kind, string mode, int id, int type)
    {
        Assert.That(BeautyTalkCommand.DialogIdByKind(kind, mode), Is.EqualTo(id));
        Assert.That(BeautyTalkCommand.DialogType(id, mode, kind), Is.EqualTo(type));
    }

    [Test]
    public void AnUnportedDialogOpensNothingButTheTalkEndsAndTheEnterRequestGoesOut()
    {
        var r = new Rig();
        Assert.That(r.Cmd.Run("BeautyShopDialog", "mirror"), Is.True);
        Assert.That(r.Sent, Is.EqualTo(new[] { new BeautyEnterRequest(77, 3) }));
        Assert.That(r.Cmd.Run("BeautyShopDialog", "nonsense"), Is.True);
        Assert.That(r.ShopShown + r.StyleShown, Is.EqualTo(0));
        Assert.That(r.Sent.Count, Is.EqualTo(1), "dialog 0 has type 0: no request");
        Assert.That(r.Ended, Is.EqualTo(2));
    }

    [Test]
    public void AnAlreadyOpenWindowStillSendsTheEnterRequestAndEndsTheTalk()
    {
        var r = new Rig();
        r.Cmd.Run("BeautyStyleDialog", "");
        r.Cmd.Run("BeautyStyleDialog", "");
        Assert.That(r.StyleShown, Is.EqualTo(1));
        Assert.That(r.Sent.Count, Is.EqualTo(2));
        Assert.That(r.Ended, Is.EqualTo(2));
    }

    [Test]
    public void TheItemColourActionWithoutACustomizeStillSendsTypeThreeAndEndsTheTalk()
    {
        var r = new Rig(customize: false);
        r.Cmd.Coloring = new ItemColoringFlow { CreateCustomize = () => null, CreateController = () => new ItemColoringDialogController() };
        r.Cmd.Run("BeautyShopDialog", "itemColor");
        Assert.That(r.Sent, Is.EqualTo(new[] { new BeautyEnterRequest(77, 3) }));
        Assert.That(r.Ended, Is.EqualTo(1));
    }

    [Test]
    public void WithoutATalkingNpcNothingOpensOrIsSentButTheTalkEnds()
    {
        var r = new Rig { Npc = 0 };
        r.Cmd.Run("BeautyStyleDialog", "");
        Assert.That((r.StyleShown, r.Sent.Count, r.Ended), Is.EqualTo((0, 0, 1)));
        var q = new Rig(customize: false);
        q.Cmd.Run("BeautyStyleDialog", "");
        Assert.That((q.Sent.Count, q.Ended), Is.EqualTo((1, 1)));
    }

    [Test]
    public void OtherDialogNamesAreNotHandled()
    {
        var r = new Rig();
        Assert.That(r.Cmd.Run("ItemEnchantDialog", "x"), Is.False);
        Assert.That(BeautyTalkCommand.Handles("ItemEnchantDialog"), Is.False);
        Assert.That(BeautyTalkCommand.Handles("BEAUTYSTYLEDIALOG"), Is.True);
    }
}
