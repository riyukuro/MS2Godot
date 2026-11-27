using System.Globalization;

namespace Ms2.Ui.Hud;

/// <summary>
/// A PC's worn gems (the PC's gem equipment at +0x1078, gemslot_2.cpp), as far as the chat balloon reads them: one item
/// per gem type, applied (CItemGem::vfunc_6) and removed (vfunc_7) through its gem. The chat balloon gem (type 3,
/// CGemChatBalloon, itemgem.cpp:668) sets the PC balloon's skin to its gem id; removing it sets "".
/// <para>
/// FUN_141270610 (:203, from PC::vfunc_12 at the model's load, pc.cpp:856) applies every slot; EquipByGemType (:280, from
/// OnBadgeEquip mode 0) stores the item and applies it; RemoveByGemType (:257, mode 1) removes it and clears the slot.
/// Applying (FUN_1412712e0, :370) needs the owner's CanUseItem and the field's gemSystem (CFieldDataCli m_bGemSystem);
/// removing (FUN_141271500, :385) needs neither. FUN_141270740 (:228), the gem worn for a type, needs CanUseItem only.
/// </para>
/// <para>
/// The owner's decision is PC::CanUseItem (pc.cpp:4683), ported in <see cref="PcItemUse"/>: the live field sets
/// <see cref="CanUse"/> to it with the packet record of each gem and its owner's state. A gem distilled without a record
/// (the tests) has no item state, so the default passes it. The buddy badge (type 7, CGemCharEffect) applies and removes
/// through <see cref="CharEffect"/>. The other gem types' apply and remove (transparency, name tag, effects, pet skin, ...)
/// are not ported here.
/// </para>
/// </summary>
public sealed class GemEquip
{
    /// <summary>GEM_CHATBALLOON (itemgem.h:127).</summary>
    public const int ChatBalloonType = 3;
    /// <summary>GEM_CHAREFFECT (itemgem.h): the buddy badge.</summary>
    public const int CharEffectType = Ms2.Ui.CharEffectGems.GemType;

    /// <summary>
    /// A worn gem item: its item id and its gem's id (CGemBase m_nGemId), with the packet's item record (the CItem the
    /// client holds) when it came from one — what PC::CanUseItem reads.
    /// </summary>
    public readonly record struct Gem(int ItemId, int GemId)
    {
        /// <summary>The packet's item record (CItem::ReadFromStream); null when the gem was distilled without one.</summary>
        public Ms2.Net.Packets.EquipItem Record { get; init; }
    }

    readonly Dictionary<int, Gem> items = new();
    readonly ChatBalloonController balloons;
    readonly ChatBalloonController.Actor owner;

    /// <summary>The field's gemSystem (WorldService::GetFieldData()->m_bGemSystem).</summary>
    public Func<bool> GemSystem = () => true;
    /// <summary>
    /// PC::CanUseItem of the owner (pc.cpp:4683, ported in <see cref="PcItemUse"/>); the default passes a gem distilled
    /// without a record, which has no item state to test.
    /// </summary>
    public Func<Gem, bool> CanUse = _ => true;
    /// <summary>The owner's CGemCharEffect (type 7, itemgem.cpp:791-830); null leaves a buddy badge unapplied.</summary>
    public Ms2.Ui.CharEffectGem CharEffect;

    public GemEquip(ChatBalloonController balloons, ChatBalloonController.Actor owner)
    {
        this.balloons = balloons;
        this.owner = owner;
    }

    /// <summary>The item worn for a gem type, when any (GetItemAt of FUN_141271290's index).</summary>
    public Gem? Item(int type) => items.TryGetValue(type, out var g) ? g : null;

    /// <summary>ReadFromStream / SetItemByGemType: the worn items of a packet, stored without applying them.</summary>
    public void Set(IEnumerable<(int Type, Gem Gem)> worn)
    {
        items.Clear();
        foreach (var (type, gem) in worn)
            if (type != 0) items[type] = gem;
        Refresh();
    }

    /// <summary>FUN_141270610: every slot applied (FUN_1412712e0).</summary>
    public void ApplyAll()
    {
        foreach (var type in items.Keys.OrderBy(t => t).ToList()) ApplySlot(type);
    }

    /// <summary>EquipByGemType: the item stored at its type, then applied.</summary>
    public void Equip(int type, Gem gem)
    {
        if (type == 0) return;
        items[type] = gem;
        ApplySlot(type);
        Refresh();
    }

    /// <summary>RemoveByGemType: the type's item removed (FUN_141271500) and its slot cleared.</summary>
    public void Remove(int type)
    {
        if (!items.TryGetValue(type, out var gem)) return;
        Unapply(type, gem);
        items.Remove(type);
        Refresh();
    }

    /// <summary>FUN_141270740(type) != 0: a gem of the type is worn and usable.</summary>
    public bool Worn(int type) => items.TryGetValue(type, out var g) && CanUse(g);

    /// <summary>FUN_141270740(type)'s gem id (CGemBase::GetGemId): 0 when no gem of the type is worn and usable.</summary>
    public int WornGemId(int type) => items.TryGetValue(type, out var g) && CanUse(g) ? g.GemId : 0;

    /// <summary>FUN_1412712e0: applied when usable and the field has the gem system.</summary>
    void ApplySlot(int type)
    {
        if (!items.TryGetValue(type, out var gem)) return;
        if (!CanUse(gem)) return;
        if (!GemSystem()) return;
        if (type == ChatBalloonType && balloons.PcBalloon(owner) is { } e)
            ChatBalloonController.SetSkin(e, ChatBalloonSkin(gem.GemId));
        if (type == CharEffectType) CharEffect?.Apply(gem.Record, gem.GemId);
    }

    /// <summary>CItemGem::vfunc_7 of the type's gem.</summary>
    void Unapply(int type, Gem gem)
    {
        if (type == ChatBalloonType && balloons.PcBalloon(owner) is { } e)
            ChatBalloonController.SetSkin(e, "");
        if (type == CharEffectType) CharEffect?.Remove();
    }

    /// <summary>OnBuddyBadge kind 3 (networkservice.cpp:1196-1199): the worn type 7 gem's item, when its uid is this one, takes
    /// the couple name (CItem::SetCoupleName); the couple system keeps the name it was made with.</summary>
    public bool SetCoupleName(long itemUid, string name)
    {
        if (!items.TryGetValue(CharEffectType, out var g) || g.Record is not { } rec || rec.Uid != itemUid) return false;
        rec.CoupleName = name ?? "";
        return true;
    }

    /// <summary>FUN_1405a7e80: the gem id as a decimal string, the skin CGemChatBalloon::vfunc_6 gives the balloon.</summary>
    public static string ChatBalloonSkin(int gemId) => gemId.ToString(CultureInfo.InvariantCulture);

    /// <summary>The open's gem test (<see cref="ChatBalloonController.Actor.BalloonGem"/>) follows the worn items.</summary>
    void Refresh() => owner.BalloonGem = Worn(ChatBalloonType);
}
