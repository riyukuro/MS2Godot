using System.Globalization;
using Ms2.Avm2;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;

namespace Ms2.Ui.Shop;

/// <summary>
/// The gacha shop's avatar (UIShopDialog::m_spPreview, an ItemPreview of mode 1 over the local player, uishopdialog.cpp:888): the render
/// target the movie loads, the mouse calls it sends, the item icons of the worn preview items (m_spEquip, a UIEquipIconContainer over
/// the movie's m_equipContainer) and the buttons that change what is worn. The worn items are <see cref="ShopAvatarPreview"/>'s; the host
/// draws them (<see cref="Preview"/>, <see cref="Avatar"/>).
/// </summary>
public sealed partial class ShopController
{
    /// <summary>m_spPreview: null until the gacha dialog is loaded and again once it is closed.</summary>
    public ShopAvatarPreview Preview { get; private set; }

    /// <summary>The avatar's turn and zoom (UIAvatarBase::vfunc_27's mouse handling); null while no avatar is loaded.</summary>
    public AvatarControl Avatar { get; private set; }

    /// <summary>UIAvatarPC::vfunc_1's CLimitCharRotate; null: no limit.</summary>
    public AvatarRotateLimit RotateLimit;

    /// <summary>
    /// The zoom ItemPreview::ResetCamera (@0x140d3e130) sets after ApplyCharCamera: m_fMinDistance 1000 (the farthest), five steps.
    /// </summary>
    public const float PreviewMinDistance = 1000f;
    public const int PreviewZoomSteps = 5;

    /// <summary>The icons of m_spEquip by slot (UIEquipIconContainer::m_28): the item and the clip's icon object.</summary>
    readonly Dictionary<int, (Item Item, object Icon)> previewIcons = new();

    /// <summary>The slots of the icons m_spEquip holds.</summary>
    public IReadOnlyCollection<int> PreviewIconSlots => previewIcons.Keys;

    /// <summary>
    /// The avatar made when the gacha dialog loads (uishopdialog.cpp:888): the preview over the player's copy, then vfunc_o178_1 (@0x141af7e10)
    /// with its model loaded: LoadRenderTarget (the movie's loadRenderTarget, which loads "img://&lt;movie&gt;:avatar/&lt;w&gt;/&lt;h&gt;"
    /// from m_mouseArea's size) and initAvatarData with showActionList (true without a preview item).
    /// </summary>
    void OpenAvatar()
    {
        // the client makes the preview only while there is none (uishopdialog.cpp:888, m_spPreview == 0)
        if (Preview != null || info.ShopType != 4 || shop == null || !Model.HasLocalPlayer) return;
        Preview = new ShopAvatarPreview(Model.Tables.ItemModels) { Gender = () => Pc.Gender, Now = ServerTime };
        Preview.Changed += OnPreviewChanged;
        Avatar = new AvatarControl(RotateLimit ?? new AvatarRotateLimit(0, 360), 0, 0, 90);
        Preview.CameraReset += Avatar.ResetCamera;
        Host.Invoke(shop, "loadRenderTarget");
        InitAvatarData(null);
    }

    /// <summary>vfunc_o178_1's initAvatarData: showActionList of the shown data (<see cref="ItemPreviewController.ShowsActionList"/>), true without data.</summary>
    void InitAvatarData(ItemData shown) =>
        Host.Invoke(shop, "initAvatarData", Host.Obj(("showActionList", shown == null || ItemPreviewController.ShowsActionList(shown))));

    /// <summary>vfunc_16 / vfunc_26 / vfunc_67: UnloadRenderTarget, the preview dropped. The host calls it too when it takes the shop's movie
    /// away without a Close (a second shop open), after it has freed the avatar's view.</summary>
    public void CloseAvatar()
    {
        if (Preview != null)
        {
            Preview.Changed -= OnPreviewChanged;
            if (Avatar != null) Preview.CameraReset -= Avatar.ResetCamera;
        }
        Preview = null;
        Avatar = null;
        previewIcons.Clear();
    }

    /// <summary>
    /// The shop's avatar events (vfunc_28 @0x141af5730): the mouse calls go to the avatar's control; "OnUnEquip" is the icon
    /// container's own event and is handled with the other shop calls.
    /// </summary>
    bool AvatarCallback(string name, object[] args) =>
        Avatar != null && name.StartsWith("OnAvatar", StringComparison.OrdinalIgnoreCase)
        && Avatar.OnCallback(name, args ?? Array.Empty<object>());

    /// <summary>The list changed through InitWithItems / FUN_140d3fa30 / ResetAvatarOutfit (vfunc_25 @0x141af8070): FUN_140d3d0d0 then
    /// UnEquipAll and AddPreviewIcon per item. TryOnItem and Unequip change the icons themselves.</summary>
    void OnPreviewChanged() => version++;

    int version;

    /// <summary>m_spEquip->UnEquipAll (@0x140e2a670).</summary>
    void UnEquipAllIcons()
    {
        previewIcons.Clear();
        if (shop != null && Host.Get(shop, "m_equipContainer") is { } clip && clip is not Undefined)
            Host.Invoke(clip, "unEquipAll");
    }

    /// <summary>The list's icons again: UnEquipAll, then AddPreviewIcon per item (vfunc_25 @0x141af8070).</summary>
    void RebuildPreviewIcons()
    {
        UnEquipAllIcons();
        if (Preview == null) return;
        foreach (var item in Preview.Items) AddPreviewIcon(item);
    }

    /// <summary>
    /// AddPreviewIcon (@0x141aff600, uishopdialog.cpp:2735): the item's ASSlot (FUN_1409c3860 with the local player and flags 0xd01);
    /// its slot is the item's, a dual-wield item's the one GetHandSlot gives; a slot of 0 is FUN_140ec1950's class; then
    /// UIEquipIconContainer::EquipItem (@0x140e2a090): a new icon (equipItem, :149) or the slot's icon changed (changeEquipItem, :170);
    /// the icon's tooltip is OnEquipmentTooltip.
    /// STILL-OPEN: FUN_140ec1950's class for FUN_14053d900 items (no named kind here) and OnEquipmentChanged, the clip's change handler.
    /// </summary>
    void AddPreviewIcon(Item item)
    {
        if (item?.Data == null || shop == null || Host.Get(shop, "m_equipContainer") is not { } clip || clip is Undefined) return;
        var slotData = new List<(string, object)>();
        ItemSlot.Build(slotData, item, Model.HasLocalPlayer, ItemPreviewController.EquipSlotFlags, Model);
        int slot = item.Slot;
        if (item.Data.EquipSubtype is 0x1f or 0x22)
        {
            // m_70 stays 0: EquipItem returns before setting it for a call with no outfit (:84)
            int hand = ShopAvatarPreview.HandSlot(item.Data, previewIcons.ContainsKey(4), previewIcons.ContainsKey(5), 0);
            if (hand != 0) slot = hand;      // the icon's slot only: the item (m_items) keeps its own (PreviewGachaItem passes the product)
        }
        if (slot == 0) slot = IconClass(item.Data);
        var value = Host.Obj(slotData.ToArray());
        object icon;
        if (previewIcons.TryGetValue(slot, out var old) && old.Icon != null)
        {
            Host.Invoke(clip, "changeEquipItem", old.Icon, slot.ToString(CultureInfo.InvariantCulture), value);
            icon = old.Icon;
        }
        else icon = Host.Invoke(clip, "equipItem", slot.ToString(CultureInfo.InvariantCulture), value);
        if (Rt.IsNullish(icon)) return;
        previewIcons[slot] = (item, icon);
        Host.Invoke(icon, "setGetTooltipFunction", new AsPlainFunction(Host.RT, new NativeMethod("getTooltip", (_, _) => Host.ToAs(PreviewIconTooltip(item)))));
        if (Host.RT.GetClass("scaleform.ms2.manager.TooltipManager") is { } tm) Host.Invoke(tm, "registerMouseEvent", icon);
    }

    /// <summary>FUN_140ec1950 (uimeratmarketcommon.cpp:1260): the class of an item that has no equip slot: 0x7b for an emotion, skill or
    /// ride item, 0x7f an install item, 0x7c a score item; 0 otherwise (package 0x79 and FUN_14053d900's 0x7d are not ported).</summary>
    static int IconClass(ItemData d) =>
        d.Type == 0x18 || d.IsSkillItem || d.Type == 0x17 || d.IsRideItem ? 0x7b : d.IsInstallItem ? 0x7f : d.IsScoreItem ? 0x7c : 0;

    /// <summary>OnEquipmentTooltip: the icon's item tooltip (the player compare is not part of the preview's).</summary>
    AsTooltip PreviewIconTooltip(Item item)
    {
        if (!Model.HasLocalPlayer) return null;
        var tip = new ItemTooltip(Model, 2) { Localize = k => Host?.Player.Translate?.Invoke(k) ?? k };
        if (!tip.Fill(item)) return null;
        tip.Data.SetTooltipAlign(5);
        return tip.Data;
    }

    // ---- the calls of the gacha dialog -------------------------------------------------------------------------------------

    /// <summary>
    /// RefreshCategories' main items (uishopdialog.cpp:2698-2731): InitWithItems, then the icons again (vfunc_25).
    /// </summary>
    void PreviewMainItemsNow(IReadOnlyList<Item> main)
    {
        if (Preview == null) return;
        Preview.InitWithItems(main);
        RebuildPreviewIcons();
        if (Preview.Items.Count == 0) Avatar?.ResetCamera();      // FUN_140d3d0d0: an empty list resets the camera (itempreview.cpp:310-352)
    }

    /// <summary>
    /// PreviewGachaItem's second half (@0x141b04050): a kind with no action list clears the icons and shows that one item (InitWithItem);
    /// the other kinds are tried on (TryOnItem); the icon is added when the item is on.
    /// </summary>
    void PreviewGachaItemNow(Item item)
    {
        if (Preview == null) return;
        bool on;
        Item worn = null;
        if (!ItemPreviewController.ShowsActionList(item.Data))
        {
            UnEquipAllIcons();
            on = Preview.InitWithItem(item);
            if (on && Preview.Items.Count > 0) worn = Preview.Items[^1];
        }
        else on = Preview.TryOnItem(item, out worn);
        InitAvatarData(item.Data);
        if (on && worn != null) AddPreviewIcon(worn);
    }

    /// <summary>onClickShowMyEquip (@0x141b04410): FUN_140d3fa30, UnEquipAll.</summary>
    void ShowMyEquipNow()
    {
        if (Preview == null) return;
        Preview.ShowMyEquip();
        UnEquipAllIcons();
    }

    /// <summary>onClickUnequipAll (@0x141b04490): ResetAvatarOutfit, UnEquipAll.</summary>
    void UnequipAllNow()
    {
        if (Preview == null) return;
        Preview.ResetAvatarOutfit();
        UnEquipAllIcons();
    }

    /// <summary>OnUnEquip (@0x141b04530): the preview's item of this uid off the avatar, the icon of the slot off the container (FUN_140e2a640).</summary>
    void UnequipNow(int slot, ulong uid)
    {
        if (Preview == null) return;
        Preview.Unequip((long)uid);
        if (previewIcons.TryGetValue(slot, out var icon) && shop != null && Host.Get(shop, "m_equipContainer") is { } clip && clip is not Undefined)
        {
            Host.Invoke(clip, "unEquipItem", icon.Icon);
            previewIcons.Remove(slot);
        }
    }
}
