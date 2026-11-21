using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// The item preview window, UIItemPreviewDialog (client/ms2/uiitempreviewdialog.cpp, UI id 0x4ec2, type name "ItemPreviewDialog",
/// movie uiitempreviewdialog) with the opening checks of ItemPreviewMgr::OpenPreview (@0x140d40120, itempreview.cpp:849). The
/// window shows the item on an avatar rendered into the movie's render target; the avatar itself is the host's
/// (<see cref="AvatarEvent"/>, <see cref="ZoomIn"/>, <see cref="ZoomOut"/>), this class holds the item state and drives the movie.
/// <para>STILL-OPEN: the avatar (ItemPreview::FUN_140d3e450 / CreateCubeAvatar / CreatePetAvatar / CreatePcAvatar, camera setup
/// and the worn-item list), the package resolve (ResolvePackage), the beauty coupon's package item and the gem's data row
/// (FUN_141b494f0), the equipment slot (UIEquipIconContainer.EquipItem 0x81 and its tooltip, FUN_1412d6330), the m_message text
/// when the item has no slot (CItemData::FUN_14053e260 / FUN_14053e2b0 unrecovered: the plain message is shown), the
/// "showActionList" rule (FUN_141b48710 unrecovered: the host's <see cref="ShowActionList"/>; none = initData is not sent),
/// the skin colour palette (FUN_1418bee30), the HUD mode's mouse cursor (ItemPreviewMgr::SetHudMode) and the top-most layer.</para>
/// </summary>
public sealed class ItemPreviewController : UiController
{
    public override string Movie => "uiitempreviewdialog";
    protected override string EntityMethod => "alignment";
    public override string TypeName => "ItemPreviewDialog";

    public const int UiId = 0x4ec2;
    /// <summary>The common strings: GetStringCommon ids of the notices and of the message.</summary>
    public const int NotPreviewableText = 0xf9, ExpiredText = 0xfa, NoSlotText = 0x9b8, PlainText = 0;

    /// <summary>FUN_141275260(id, 1, 1): the system notice text.</summary>
    public Action<string> Notice = _ => { };
    /// <summary>The avatar: a zoom command (ItemPreview::AvatarZoomIn / AvatarZoomOut), the cube's apply
    /// (ApplyPreviewToCube) and the movie's other avatar events; true when the host handled the event.</summary>
    public Action ZoomIn = () => { }, ZoomOut = () => { }, ApplyToCube = () => { };
    public Func<string, object[], bool> AvatarEvent = (_, _) => false;
    /// <summary>FUN_141b487f0 for the shown data (the action list); null = the host has not ported it.</summary>
    public Func<ItemData, bool> ShowActionList;
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
    /// <summary>The message the window shows (m_message), "" before the movie is up.</summary>
    public string Message { get; private set; } = "";
    bool closing;
    GameTables tables;

    /// <summary>ItemPreviewMgr::OpenPreview's checks (the model is the opener's: the window has no host yet) and InitWithItem: false (0) for no item, no local player, a row that must not
    /// be previewed (notice 0xf9) or an expired item (notice 0xfa). On true the window may be shown.</summary>
    public bool Open(UiModel model, Item item, int arg)
    {
        if (item == null || model == null || !model.HasLocalPlayer) return false;
        tables = model.Tables;
        var data = item.Data;
        if (data == null) return false;
        if (data.DoNotPreview != 0) { Notice(tables.Common(NotPreviewableText)); return false; }
        if (IsExpired(item)) { Notice(tables.Common(ExpiredText)); return false; }
        Arg = arg;
        return InitWithItem(item);
    }

    /// <summary>CItem::IsExpired (item.cpp:1303): an expiry time set and not after the server time.</summary>
    public bool IsExpired(Item item) => item.ExpiryTime > 0 && item.ExpiryTime <= Now();

    /// <summary>ItemPreview::InitWithItem (@0x140d3bc90): the shown item is a copy of the asked one. The package, gem and
    /// beauty-coupon paths are not ported, so those items show nothing (false).</summary>
    bool InitWithItem(Item item)
    {
        var data = item.Data;
        if (data.FunctionName == "ItemChangeBeauty" || data.IsGemItem) return false;
        Source = item;
        Shown = item;
        return true;
    }

    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (Source == null) { Close(); return; }       // m_30 == 0: vfunc_67 (the preview cleared)
        LoadRenderTarget();
        ShowMessage();
        InitData();
    }

    /// <summary>vfunc_25's m_message: the plain text (the no-slot text is STILL-OPEN, see the class doc).</summary>
    void ShowMessage()
    {
        Message = tables?.Common(PlainText) ?? "";
        if (Host?.Child("m_message") is { } m) Host.Set(m, "text", Message);
    }

    /// <summary>vfunc_o178_1 (the avatar is up): initData {showActionList}.</summary>
    void InitData()
    {
        if (Host?.Entity == null || Data == null || ShowActionList == null) return;
        Host.Invoke(Host.Entity, "initData", Host.Obj(("showActionList", ShowActionList(Data))));
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
