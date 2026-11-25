using Ms2.Avm2;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// The item preview window, UIItemPreviewDialog (client/ms2/uiitempreviewdialog.cpp, UI id 0x4ec2, type name "ItemPreviewDialog",
/// movie uiitempreviewdialog) with the opening checks of ItemPreviewMgr::OpenPreview (@0x140d40120, itempreview.cpp:849). The
/// window shows the item on an avatar rendered into the movie's render target; the avatar itself is the host's
/// (<see cref="AvatarEvent"/>, <see cref="ZoomIn"/>, <see cref="ZoomOut"/>), this class holds the item state and drives the movie:
/// the message, the item's equipment slot and its tooltip, and initData.
/// <para>STILL-OPEN: the avatar's model (FUN_140d3e450's UIAvatarPC with the player's or the other gender's look, CreateCubeAvatar,
/// CreatePetAvatar, the camera setup and the worn-item list; <see cref="Avatar"/> is only which one the client makes), the
/// package resolve (ResolvePackage), the beauty coupon's package item and the gem's data row (FUN_141b494f0) and gem
/// (CreatePreviewItem's CreateGem), the skin colour palette of initData (FUN_1418bee30 for an AvatarPC of type 1), the
/// HUD mode's mouse cursor (ItemPreviewMgr::SetHudMode) and the top-most layer.</para>
/// </summary>
public sealed class ItemPreviewController : UiController
{
    public override string Movie => "uiitempreviewdialog";
    protected override string EntityMethod => "alignment";
    public override string TypeName => "ItemPreviewDialog";
    /// <summary>UIItemPreviewDialog's ctor (@0x1412d5890, uiitempreviewdialog.cpp:64) has no SetTweenEnabled: m_c4 stays 0, so CloseDialog
    /// unloads at once (FUN_141924330, uidialog.cpp:393) and a following OpenDialog loads it again.</summary>
    protected override bool TweenEnabled => false;

    public const int UiId = 0x4ec2;
    /// <summary>The common strings: GetStringCommon ids of the notices and of the message.</summary>
    public const int NotPreviewableText = 0xf9, ExpiredText = 0xfa, NoSlotText = 0x9b8, PlainText = 0;
    /// <summary>FUN_1412d6330's slot: UIEquipIconContainer::EquipItem(0x81, ...) with an ASSlot(2) built by FUN_1409c3860(item,
    /// the local player, 0xd01).</summary>
    public const int EquipSlotIndex = 0x81, EquipSlotFlags = 0xd01;

    /// <summary>The avatar ItemPreview::InitWithItem (@0x140d3bc90, itempreview.cpp:116) makes for the shown data.</summary>
    public enum AvatarKind { None, Pc, Cube, Pet }

    /// <summary>FUN_141275260(id, 1, 1): the system notice text.</summary>
    public Action<string> Notice = _ => { };
    /// <summary>The avatar: a zoom command (ItemPreview::AvatarZoomIn / AvatarZoomOut), the cube's apply
    /// (ApplyPreviewToCube) and the movie's other avatar events; true when the host handled the event.</summary>
    public Action ZoomIn = () => { }, ZoomOut = () => { }, ApplyToCube = () => { };
    public Func<string, object[], bool> AvatarEvent = (_, _) => false;
    /// <summary>The server time (CItem::IsExpired).</summary>
    public Func<long> Now = () => 0;
    public event Action Closed;

    /// <summary>ItemPreviewMgr::m_nArg (GetArg): the caller's mode (the shop passes 1).</summary>
    public int Arg { get; private set; }
    /// <summary>ItemPreview::m_spItem (GetSourceItem): the item that was asked for.</summary>
    public Item Source { get; private set; }
    /// <summary>ItemPreview::m_spPreview (GetPreviewItem): the item shown, null when nothing is.</summary>
    public Item Shown { get; private set; }
    /// <summary>ItemPreview::GetPreviewData: the shown item's data row.</summary>
    public ItemData Data => Shown?.Data;
    /// <summary>ItemPreview::m_spAvatar's kind; None: the client makes no avatar, so its listener never sends initData.</summary>
    public AvatarKind Avatar { get; private set; }
    /// <summary>The message the window shows (m_message), "" before the movie is up.</summary>
    public string Message { get; private set; } = "";
    /// <summary>The equipment slot's object (the slot FUN_1409c3860 built), null before the movie shows it.</summary>
    public IReadOnlyList<(string, object)> EquipSlot { get; private set; }
    bool closing;
    UiModel model;

    /// <summary>ItemPreviewMgr::OpenPreview's checks (the model is the opener's: the window has no host yet) and InitWithItem: false (0) for no item, no local player, a row that must not
    /// be previewed (notice 0xf9) or an expired item (notice 0xfa). On true the window may be shown.</summary>
    public bool Open(UiModel model, Item item, int arg)
    {
        if (item == null || model == null || !model.HasLocalPlayer) return false;
        this.model = model;
        var data = item.Data;
        if (data == null) return false;
        if (data.DoNotPreview != 0) { Notice(model.Tables.Common(NotPreviewableText)); return false; }
        if (IsExpired(item)) { Notice(model.Tables.Common(ExpiredText)); return false; }
        Arg = arg;
        return InitWithItem(item);
    }

    /// <summary>CItem::IsExpired (item.cpp:1303): an expiry time set and not after the server time.</summary>
    public bool IsExpired(Item item) => item.ExpiryTime > 0 && item.ExpiryTime <= Now();

    /// <summary>ItemPreview::InitWithItem (@0x140d3bc90): the avatar for the data, and the shown item is a copy of the asked one.
    /// The package, gem and beauty-coupon paths are not ported, so those items show nothing (false).</summary>
    bool InitWithItem(Item item)
    {
        var data = item.Data;
        if (data.FunctionName == "ItemChangeBeauty" || data.IsGemItem) return false;
        Avatar = AvatarFor(data);
        Source = item;
        Shown = item;
        return true;
    }

    /// <summary>InitWithItem's choice (itempreview.cpp:141-146): FUN_140d3de80's kinds get the PC avatar (FUN_140d3e450), an
    /// install item the cube (CreateCubeAvatar), a pet the pet (CreatePetAvatar); anything else none.</summary>
    public static AvatarKind AvatarFor(ItemData d) =>
        WearsOnPc(d) ? AvatarKind.Pc : d.IsInstallItem ? AvatarKind.Cube : d.IsPetItem ? AvatarKind.Pet : AvatarKind.None;

    /// <summary>ItemPreview::FUN_140d3de80 (itempreview.cpp:452): an equipment item (FUN_14053cbc0), type 0x18 (FUN_14053ce40),
    /// a skill item, type 0x17 (FUN_14053cd50), a ride item, a gem of subtype 5, 8 or 6 (FUN_14053e900), or an ItemChangeBeauty
    /// coupon whose package has items (InitWithItem refuses those here, so it is left out).</summary>
    static bool WearsOnPc(ItemData d) =>
        d.IsEquip || d.Type == 0x18 || d.IsSkillItem || d.Type == 0x17 || d.IsRideItem || d.GemSubtype is 5 or 8 or 6;

    /// <summary>FUN_141b48710 (uiugcitemeditdialog.cpp:2300): type 0x18, a skill item, type 0x17 or a ride item.</summary>
    static bool IsActionItem(ItemData d) => d.Type == 0x18 || d.IsSkillItem || d.Type == 0x17 || d.IsRideItem;

    /// <summary>FUN_141b487f0 (uiugcitemeditdialog.cpp:2311): no action list for FUN_141b48710's kinds or a gem of subtype 5, 8
    /// or 6; true for anything else.</summary>
    public static bool ShowsActionList(ItemData d) => !IsActionItem(d) && d.GemSubtype is not (5 or 8 or 6);

    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (Source == null) { Close(); return; }       // m_30 == 0: vfunc_67 (the preview cleared)
        LoadRenderTarget();
        ShowItem();
        InitData();
    }

    /// <summary>
    /// vfunc_25 (@0x1412d5cb0, :132), the avatar's draw into the render target: m_message is the no-slot text 0x9b8 when the
    /// caller's mode is not 0 and the shown item's model has a palette (CItemData::FUN_14053e260) with no first default colour
    /// (FUN_14053e2b0(0) == -1), else common 0; then the source item's equipment slot (FUN_1412d6330). The client runs it
    /// at each draw of the avatar; with no avatar here it runs once, when the movie is up.
    /// </summary>
    void ShowItem()
    {
        bool noSlot = Shown != null && Arg != 0 && model.Tables.ItemModels.ForItem(Shown.Data) is { Palette: not 0 } m && m.DefaultColor(0) == -1;
        Message = model.Tables.Common(noSlot ? NoSlotText : PlainText) ?? "";
        if (Host?.Child("m_message") is not { } text) return;
        Host.Set(text, "text", Message);
        ShowEquipSlot(Source);
    }

    /// <summary>
    /// FUN_1412d6330 (:221): nothing without the item or m_equip (OnLoadComplete makes it over "m_equipContainer"). The slot is
    /// an ASSlot(2) from FUN_1409c3860(item, the local player, 0xd01); UIEquipIconContainer::EquipItem(0x81) makes the icon with
    /// the clip's equipItem(0x81, slot) (CreateEquipIcon, uiequipiconcontainer.cpp:149; a later draw's ChangeEquipIcon :170
    /// does not happen here, see <see cref="ShowItem"/>); then the icon's tooltip is GetItemTooltip
    /// (ASEquipIcon::FUN_1409b9240: ASUIComponent::SetGetTooltipFunction, asuicomponent.cpp:202, setGetTooltipFunction then
    /// RegisterTooltip).
    /// </summary>
    void ShowEquipSlot(Item item)
    {
        if (item == null || Host.Child("m_equipContainer") is not { } clip) return;
        var slot = new List<(string, object)>();
        ItemSlot.Build(slot, item, model.HasLocalPlayer, EquipSlotFlags, model);
        EquipSlot = slot;
        var value = Host.Obj(slot.ToArray());
        var icon = Host.Invoke(clip, "equipItem", (uint)EquipSlotIndex, value);
        if (Rt.IsNullish(icon)) return;
        Host.Invoke(icon, "setGetTooltipFunction", new AsPlainFunction(Host.RT, new NativeMethod("getTooltip", (_, _) => Host.ToAs(GetItemTooltip()))));
        if (Host.RT.GetClass("scaleform.ms2.manager.TooltipManager") is { } tm) Host.Invoke(tm, "registerMouseEvent", icon);   // RegisterTooltip: FUN_1422c6380
    }

    /// <summary>GetItemTooltip (@0x1412d66e0, :240): the source item with a local player, ASTooltip(player, 2, 2) filled by
    /// FUN_1409e0d40, align 5; null (no return value) otherwise.</summary>
    public AsTooltip GetItemTooltip()
    {
        if (Source == null || model == null || !model.HasLocalPlayer) return null;
        var tip = new ItemTooltip(model, 2) { Localize = k => Host?.Player.Translate?.Invoke(k) ?? k };
        if (!tip.Fill(Source)) return null;
        tip.Data.SetTooltipAlign(5);
        return tip.Data;
    }

    /// <summary>vfunc_o178_1 (:175), the avatar's "loaded" event: the render target again, then initData; for an AvatarPC its
    /// showActionList (FUN_141b487f0 of the shown data, true without one). No avatar, no event: nothing is sent.</summary>
    void InitData()
    {
        if (Host?.Entity == null || Avatar == AvatarKind.None) return;
        var data = Avatar == AvatarKind.Pc && Data != null ? Host.Obj(("showActionList", ShowsActionList(Data))) : Host.Obj();
        Host.Invoke(Host.Entity, "initData", data);
    }

    /// <summary>LoadRenderTarget / UnloadRenderTarget (@0x1412d6270, @0x1412d62d0).</summary>
    void LoadRenderTarget() { if (Host?.Entity != null) Host.Invoke(Host.Entity, "loadRenderTarget"); }
    void UnloadRenderTarget() { if (Host?.Entity != null) Host.Invoke(Host.Entity, "unloadRenderTarget"); }

    /// <summary>vfunc_28: the movie's avatar events go to the preview (ItemPreviewMgr::HandleAvatarEvent).</summary>
    public override bool OnCallback(string name, object[] args) => AvatarEvent(name, args);

    /// <summary>vfunc_26 / vfunc_67: the render target released, the preview cleared.</summary>
    public override void Close()
    {
        if (closing) return;
        closing = true;
        UnloadRenderTarget();
        Source = Shown = null;
        base.Close();
        Closed?.Invoke();
    }
}
