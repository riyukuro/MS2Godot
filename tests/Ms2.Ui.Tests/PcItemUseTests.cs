using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// PC::CanUseItem (pc.cpp:4683) as the chat balloon gem's gate reads it: the item's own state (CItem::FUN_140528980,
/// item.cpp:1717), the PC bang / VIP / wedding requirements and the owner bind, each on the packet's item record.
/// </summary>
public class PcItemUseTests
{
    const long Now = 1_000_000;

    static PcItemUse.Pc Pc(long charId = 7, byte grade = 0, long vipTime = 0, short wedding = 0) => new()
    {
        CharacterId = charId, PcBangGrade = grade, PremiumTime = vipTime, WeddingState = wedding,
        ServerTime = () => Now,
    };

    static ItemData Data(bool pcBang = false, bool vip = false, bool wedding = false,
        int type = 0, int subtype = 0, int param2 = 0) => new()
    {
        PCBang = pcBang, Vip = vip, Wedding = wedding, Type = type, Subtype = subtype, Param2 = param2,
    };

    [Test]
    public void NoItemOrNoOwnerIsNeverUsable()
    {
        Assert.That(PcItemUse.CanUseItem(null, Data(), Pc()), Is.False);
        Assert.That(PcItemUse.CanUseItem(new EquipItem(), Data(), null), Is.False);
        Assert.That(PcItemUse.CanUseItem(new EquipItem(), null, Pc()), Is.False, "an id the item data does not know is never created (CItem::FUN_140523c40)");
    }

    [Test]
    public void AnExpiredItemIsUsableOnlyAsAPetOrAMaidItem()
    {
        var expired = new EquipItem { ExpiryTime = Now };
        Assert.That(PcItemUse.CanUseItem(expired, Data(), Pc()), Is.False);
        // a pet item (type 0xb, itemdata.cpp:675) expires but stays usable, as does a maid item (type 6 subtype 7 with a param2)
        Assert.That(PcItemUse.CanUseItem(expired, Data(type: 0xb), Pc()), Is.True);
        Assert.That(PcItemUse.CanUseItem(expired, Data(type: 6, subtype: 7, param2: 5), Pc()), Is.True);
        Assert.That(PcItemUse.CanUseItem(expired, Data(type: 6, subtype: 7, param2: 0), Pc()), Is.False);
        // the expiry is inclusive: a time in the future is not expired yet
        Assert.That(PcItemUse.CanUseItem(new EquipItem { ExpiryTime = Now + 1 }, Data(), Pc()), Is.True);
        Assert.That(PcItemUse.CanUseItem(new EquipItem { ExpiryTime = 0 }, Data(), Pc()), Is.True);
    }

    [Test]
    public void AScoreItemNeedsAUseLeft()
    {
        var noUses = new EquipItem { RemainUses = 0 };
        Assert.That(PcItemUse.CanUseItem(noUses, Data(type: 0xc), Pc()), Is.False);
        Assert.That(PcItemUse.CanUseItem(new EquipItem { RemainUses = 1 }, Data(type: 0xc), Pc()), Is.True);
        Assert.That(PcItemUse.CanUseItem(noUses, Data(), Pc()), Is.True, "a gem is never a score item");
    }

    [Test]
    public void ThePcBangRequirementFollowsTheGradeByte()
    {
        var it = new EquipItem();
        Assert.That(PcItemUse.CanUseItem(it, Data(pcBang: true), Pc()), Is.False);
        Assert.That(PcItemUse.CanUseItem(it, Data(pcBang: true), Pc(grade: 1)), Is.True);
        Assert.That(PcItemUse.CanUseItem(it, Data(pcBang: false), Pc()), Is.True);
    }

    [Test]
    public void TheVipRequirementFollowsThePremiumTime()
    {
        var it = new EquipItem();
        Assert.That(PcItemUse.CanUseItem(it, Data(vip: true), Pc()), Is.False, "no premium time left");
        Assert.That(PcItemUse.CanUseItem(it, Data(vip: true), Pc(vipTime: Now)), Is.False, "the time itself is expired");
        Assert.That(PcItemUse.CanUseItem(it, Data(vip: true), Pc(vipTime: Now + 1)), Is.True);
        Assert.That(PcItemUse.CanUseItem(it, Data(vip: false), Pc()), Is.True);
    }

    [Test]
    public void TheWeddingRequirementFollowsTheWeddingState()
    {
        var it = new EquipItem();
        Assert.That(PcItemUse.CanUseItem(it, Data(wedding: true), Pc(wedding: 1)), Is.False);
        Assert.That(PcItemUse.CanUseItem(it, Data(wedding: true), Pc(wedding: 3)), Is.True);
        Assert.That(PcItemUse.CanUseItem(it, Data(wedding: false), Pc()), Is.True);
    }

    /// <summary>The wedding manager after a proxy packet of this state (Wedding mode 0, WeddingProxy::Read).</summary>
    internal static WeddingManagerState Manager(int state, byte mode = 0)
    {
        var m = new WeddingManagerState();
        m.OnPacket(new WeddingPacket(mode) { Proxy = new WeddingProxyState(0, (byte)state, 0, 0, default, default, 0, "", "", "", "", Array.Empty<WeddingLogEntry>()) });
        return m;
    }

    [Test]
    public void TheOwnPlayersWeddingRequirementIsTheManagersNotTheFieldUsersState()
    {
        // MyPC::vfunc_146 (mypc.cpp:1409) is WeddingManager::IsInProgress (m_proxy.m_nState 2, 3, 4); PC::vfunc_146 (pc.cpp:2218) is m_n1220
        var it = new EquipItem();
        foreach (int state in new[] { 0, 1, 2, 3, 4, 5 })
        {
            bool on = state is 2 or 3 or 4;
            var own = Pc(wedding: (short)(on ? 0 : 3)); own.Manager = Manager(state);
            Assert.That(PcItemUse.CanUseItem(it, Data(wedding: true), own), Is.EqualTo(on), $"own player, manager state {state}, field state {own.WeddingState}");
            Assert.That(PcItemUse.CanUseItem(it, Data(wedding: true), Pc(wedding: (short)state)), Is.EqualTo(on), $"another player, field state {state}");
        }
        Assert.That(new WeddingManagerState().IsInProgress, Is.False, "WeddingProxy's constructor sets the state 0 (weddingproxy.cpp:186)");
    }

    [Test]
    public void OnlyTheProxyModesSetTheManagersState()
    {
        // WeddingManager::OnProxy (mode 0) and OnProposeAccepted (mode 6) run m_proxy.Read; the other modes leave it
        Assert.That(Manager(3, 0).IsInProgress, Is.True);
        Assert.That(Manager(4, 6).IsInProgress, Is.True);
        var m = Manager(2);
        Assert.That(m.OnPacket(new WeddingPacket(2) { Result = 1 }), Is.False);
        Assert.That(m.OnPacket(new WeddingPacket(0x17)), Is.False);
        Assert.That(m.ProxyState, Is.EqualTo(2));
        Assert.That(m.OnPacket(new WeddingPacket(1) { Proxy = null }), Is.False);
        Assert.That(m.OnPacket(new WeddingPacket(0) { Proxy = new WeddingProxyState(0, 1, 0, 0, default, default, 0, "", "", "", "", Array.Empty<WeddingLogEntry>()) }), Is.True);
        Assert.That(m.IsInProgress, Is.False, "a proxy packet of state 1 ends it");
    }

    [Test]
    public void TheOwnerBindIsTheTransfersAndTheSocketGems()
    {
        var bound = new EquipItem { Transfer = new ItemTransfer { HasBinding = true, BindingId = 99 } };
        Assert.That(PcItemUse.CanUseItem(bound, Data(), Pc(charId: 7)), Is.False);
        Assert.That(PcItemUse.CanUseItem(new EquipItem { Transfer = new ItemTransfer { HasBinding = true, BindingId = 7 } }, Data(), Pc(charId: 7)), Is.True);
        Assert.That(PcItemUse.CanUseItem(new EquipItem(), Data(), Pc(charId: 7)), Is.True, "no binding passes anyone");
        // a socketed gem bound to another character (AllGemsUsableByCharacter, itemsocket.cpp:867)
        var other = new ItemSocket();
        other.Gems.Add(new SocketGem { GemId = 1, HasBind = true, BindCharacterId = 99 });
        Assert.That(PcItemUse.CanUseItem(new EquipItem { Socket = other }, Data(), Pc(charId: 7)), Is.False);
        var gem = new ItemSocket();    // every gem of the socket must pass, as the client's loop does
        gem.Gems.Add(new SocketGem { GemId = 1, HasBind = true, BindCharacterId = 7 });
        gem.Gems.Add(new SocketGem { GemId = 0, HasBind = true, BindCharacterId = 99 });   // an empty slot
        gem.Gems.Add(new SocketGem { GemId = 2, HasBind = true, BindCharacterId = 0 });    // a bind with id 0 is not bound
        gem.Gems.Add(null);
        Assert.That(PcItemUse.CanUseItem(new EquipItem { Socket = gem }, Data(), Pc(charId: 7)), Is.True,
            "an empty slot, an id-0 bind and the owner's bound gem pass");
    }

    [Test]
    public void AGemTheOwnerCannotUseIsNotWornAndNotApplied()
    {
        var owner = new ChatBalloonController.Actor { ObjectId = 3, IsPc = true };
        var g = new GemEquip(new ChatBalloonController(), owner)
        { CanUse = x => PcItemUse.CanUseItem(x.Record, Data(), Pc()) };
        g.Equip(GemEquip.ChatBalloonType, new GemEquip.Gem(1, 42)
        { Record = new EquipItem { Transfer = new ItemTransfer { HasBinding = true, BindingId = 99 } } });
        Assert.That(owner.BalloonGem, Is.False, "a gem bound to another character is neither worn nor applied");
        Assert.That(g.Item(GemEquip.ChatBalloonType)?.GemId, Is.EqualTo(42), "the item is still stored, as EquipByGemType stores it first");
        g.Equip(GemEquip.ChatBalloonType, new GemEquip.Gem(1, 43) { Record = new EquipItem() });
        Assert.That(owner.BalloonGem, Is.True);
    }
}
