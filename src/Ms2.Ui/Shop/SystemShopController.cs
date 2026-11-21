using System.Globalization;
using Ms2.Net.Packets;
using Ms2.Ui.Items;

namespace Ms2.Ui.Shop;

/// <summary>
/// Client system-shop open/toggle flow shared by <see cref="SystemShopController"/> and the HUD:
/// <see cref="OnSystemShop"/> (CSceneStateGame::OnSystemShop, scenestategame.cpp:7928) and
/// <see cref="ToggleShopTab"/> (unit_141347b10.cpp:468).
/// </summary>
public static class SystemShopFlow
{
    /// <summary>PlaySystemSound key (s_strBuildOpenShop, unit_141347b10.cpp:98, :491).</summary>
    public const string OpenSound = "System_BuildOpenShop";

    /// <summary>
    /// CSceneStateGame::OnSystemShop (@0x141733660, scenestategame.cpp:7928): ignores type 0; when !ok shows
    /// s_shop_no_star_point_event for type 8; when ok opens the shop dialog (0x4e3b) and sets its SortKind.
    /// </summary>
    public static bool OnSystemShop(
        SystemShopPacket packet,
        ShopController shop,
        Func<string, string> commonByKey,
        Action openShop,
        Action<Hud.MessageBoxData> showBox)
    {
        if (packet == null || packet.Type == 0) return false;
        if (!packet.Ok)
        {
            if (packet.Type == 8)
            {
                string msg = commonByKey?.Invoke("s_shop_no_star_point_event") ?? "";
                showBox?.Invoke(Hud.MessageBoxQueue.Create(0, msg, null, ""));
            }
            return false;
        }
        openShop?.Invoke();
        if (shop != null) shop.SortKind = packet.Type;
        return true;
    }

    /// <summary>
    /// ToggleShopTab (@0x1413499b0, unit_141347b10.cpp:468): toggles or switches a system shop tab (Outbound 0x65).
    /// When the shop is already open on <paramref name="tab"/>, closes the shop (whose Close sends state 0) and
    /// inventory and returns 0; otherwise closes any open different shop/inventory, sends SystemShop(tab, 1) and
    /// plays <see cref="OpenSound"/>, returning 1.
    /// </summary>
    public static int ToggleShopTab(
        int tab,
        ShopController shop,
        bool shopOpen,
        Action closeShop,
        Action closeInventory,
        Action<Ms2.Net.Wire.IOutPacket> send,
        Action<string> sound = null)
    {
        if (shop == null) return 0;
        bool open = true;
        if (shopOpen)
        {
            if (shop.SortKind == tab)
            {
                open = false;
            }
            else
            {
                closeShop?.Invoke();
                closeInventory?.Invoke();
            }
        }
        if (open)
        {
            send?.Invoke(new SystemShopRequest((byte)tab, 1));
            sound?.Invoke(OpenSound);
            return 1;
        }
        closeShop?.Invoke();
        closeInventory?.Invoke();
        return 0;
    }
}

/// <summary>
/// UISystemShopDialog (client/ms2/uisystemshopdialog.cpp; UI id 0x4f07, movie uisystemshopdialog): always-on helper
/// popup for fishing (mode 4: system shop toggle, auto-fishing extend/cancel, bait slot) and instrument play (mode 5:
/// auto-play extend/cancel).
/// <para>STILL-OPEN: CSmartPushTable / CGameRule auto-action smart-push package catalog wiring and buff-icon block on
/// message box 0x24 (populated via <see cref="AutoFishingPackages"/> / <see cref="AutoInstrumentPackages"/> hooks).</para>
/// </summary>
public sealed class SystemShopController : UiController
{
    public override string Movie => "uisystemshopdialog";
    protected override string EntityMethod => "setShop";
    public override string TypeName => "SystemShopDialog";   // GetTypeName (@0x140fa4250)
    public override bool AlwaysOn => true;                   // ctor SetFlag(0, 1) (uisystemshopdialog.cpp:201)

    /// <summary>The ui id (vfunc_48 @0x140fa7f30).</summary>
    public const int UiId = 0x4f07;

    /// <summary>One duration/price row of a smart-push auto-action package (uisystemshopdialog.cpp:295/:347).</summary>
    public sealed record AutoActionPackage(uint DurationMinutes, long Merat, long Meso);

    /// <summary>m_nMode (SetMode @0x140fa5ba0): 4 = fishing, 5 = play instrument.</summary>
    public int Mode { get; set; }

    /// <summary>The bait item currently bound to the fishing slot ("SystemShopDialog", flags 0x11).</summary>
    public Item BaitItem { get; private set; }

    /// <summary>ToggleShopTab(mode) for mode 4 action 0 (uisystemshopdialog.cpp:257).</summary>
    public Func<int, int> ToggleShop;
    /// <summary>FUN_140d73c60: shows a modal message box (defaults to <see cref="Hud.MessageBoxQueue.Show"/>).</summary>
    public Action<Hud.MessageBoxData> ShowBox = Hud.MessageBoxQueue.Show;

    // Fishing hooks (CFishingManager / CSmartPushAutoFishing / PCBase buffs)
    public Func<int> GetAutoFishingCount = () => 0;
    public Action<int> SetAutoFishingCount;
    public Func<bool> HasAutoFishingBuff = () => false;
    public Func<int> GetBaitId = () => 0;
    public Action<int> SetBaitId;
    public Func<int, Item> FindBaitItem;
    public Func<Item, bool> CanUseBait = _ => true;
    public Func<int> AutoFishingBuffId = () => 0;
    public Func<IReadOnlyList<AutoActionPackage>> AutoFishingPackages;
    public Func<uint> AutoFishingSaleRate = () => 0;
    public Func<bool> IsFirstUseRedMeratFishing = () => false;
    public Action<int> SelectAutoFishingPackage;

    // Instrument hooks (CInstrumentManager / CSmartPushAutoPlayInstrument)
    public Func<uint> GetAutoInstrumentCount = () => 0;
    public Action<uint> SetAutoInstrumentCount;
    public Func<bool> CanPlayEnsemble = () => false;
    public Func<int> AutoInstrumentBuffId = () => 0;
    public Func<IReadOnlyList<AutoActionPackage>> AutoInstrumentPackages;
    public Func<uint> AutoInstrumentSaleRate = () => 0;
    public Func<bool> IsFirstUseRedMeratInstrument = () => false;
    public Action<int> SelectAutoInstrumentPackage;

    string Common(string key, params object[] args) =>
        args.Length == 0 ? Model?.Tables.Common(key) ?? "" : Ms2.Quest.ClientFormat.Format(Model?.Tables.Common(key) ?? "", args);

    string Common(int id) => Model?.Tables.Common(id) ?? "";

    /// <summary>UISystemShopDialog::OnLoadComplete (@0x140fa42a0, uisystemshopdialog.cpp:220).</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (Mode == 4) RefreshFishing();
        else if (Mode == 5) RefreshInstrument();
    }

    /// <summary>UISystemShopDialog::vfunc_67 (@0x140fa43c0, uisystemshopdialog.cpp:233): clears bait binding then closes.</summary>
    public override void Close()
    {
        BaitItem = null;
        base.Close();
    }

    /// <summary>Fishing mode setup (@0x140fa5bc0, uisystemshopdialog.cpp:399): binds bait slot (flags 0x11), invokes
    /// setShop(desc, title, extendTitle, "SystemShopDialog") and setIcon(1, cancel).</summary>
    public void RefreshFishing()
    {
        if (Model == null || !Model.HasLocalPlayer || Mode != 4 || Host?.Entity == null) return;
        int baitId = GetBaitId();
        BaitItem = baitId != 0 ? FindBaitItem?.Invoke(baitId) : null;
        Host.Push(TypeName);
        int cancel = 0;
        string title = Common("s_system_shop_title_fishing");
        string desc = Common("s_system_shop_desc_fishing");
        string extend = "";
        if (HasAutoFishingBuff())
        {
            cancel = GetAutoFishingCount() > 0 ? 1 : 0;
            extend = Common(cancel != 0 ? "s_system_shop_title_extend_autofishing_cancel" : "s_system_shop_title_extend_autofishing");
        }
        Host.Invoke(Host.Entity, "setShop", desc, title, extend, TypeName);
        Host.Invoke(Host.Entity, "setIcon", 1, cancel);
    }

    /// <summary>Instrument mode setup (@0x140fa64e0, uisystemshopdialog.cpp:470): invokes setShop(desc, title, "", "")
    /// and setIcon(0, cancel).</summary>
    public void RefreshInstrument()
    {
        if (Model == null || !Model.HasLocalPlayer || Mode != 5 || Host?.Entity == null) return;
        int cancel = 0;
        string title = Common("s_system_shop_title_extend_play_instrument");
        string desc = Common("s_system_shop_desc_play_instrument");
        if (CanPlayEnsemble())
        {
            cancel = GetAutoInstrumentCount() > 0 ? 1 : 0;
            if (cancel != 0) title = Common("s_system_shop_title_extend_play_instrument_cancel");
        }
        Host.Invoke(Host.Entity, "setShop", desc, title, "", "");
        Host.Invoke(Host.Entity, "setIcon", 0, cancel);
    }

    /// <summary>UISystemShopDialog::vfunc_28 (@0x140fa44f0, uisystemshopdialog.cpp:243): handles OnShop(action).</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (!name.Equals("OnShop", StringComparison.OrdinalIgnoreCase)) return false;
        if (args == null || args.Length == 0) return true;
        int action = ToInt(args[0]);
        if (Mode == 4)
        {
            if (action == 0)
            {
                ToggleShop?.Invoke(Mode);
            }
            else if (action == 1)
            {
                if (GetAutoFishingCount() > 0)
                {
                    var box = Hud.MessageBoxQueue.Create(1, Common("s_msg_autofishing_extend_cancel_desc"), null, "");
                    box.SetCheckbox0(Common(0xc65), true);
                    box.Callback = OnCancelAutoFishing;
                    ShowBox?.Invoke(box);
                }
                else
                {
                    ShowExtendBox(
                        AutoFishingPackages?.Invoke(),
                        AutoFishingBuffId(),
                        "s_msg_autofishing_extend_showcurrentduration_desc",
                        Model?.Tables.ExtendAutoFishMaxCount ?? 8,
                        "s_auto_action_fishing",
                        IsFirstUseRedMeratFishing(),
                        AutoFishingSaleRate(),
                        OnConfirmAutoFishing);
                }
            }
        }
        else if (Mode == 5 && action == 0)
        {
            if (GetAutoInstrumentCount() > 0)
            {
                var box = Hud.MessageBoxQueue.Create(1, Common("s_msg_auto_play_instrument_extend_cancel_desc"), null, "");
                box.SetCheckbox0(Common("s_system_auto_play_instrument_extend"), true);
                box.Callback = OnCancelAutoInstrument;
                ShowBox?.Invoke(box);
            }
            else
            {
                ShowExtendBox(
                    AutoInstrumentPackages?.Invoke(),
                    AutoInstrumentBuffId(),
                    "s_msg_auto_play_instrument_extend_showcurrentduration_desc",
                    Model?.Tables.ExtendAutoPlayInstrumentMaxCount ?? 8,
                    "s_auto_action_play_instrument",
                    IsFirstUseRedMeratInstrument(),
                    AutoInstrumentSaleRate(),
                    OnConfirmAutoInstrument);
            }
        }
        return true;
    }

    void ShowExtendBox(
        IReadOnlyList<AutoActionPackage> packages,
        int buffId,
        string descKey,
        uint maxCount,
        string actionNameKey,
        bool firstUseRedMerat,
        uint saleRate,
        Func<int, Hud.MessageBoxData, bool> callback)
    {
        if (packages == null || buffId == 0) return;
        var box = Hud.MessageBoxQueue.Create(0x24, Common(descKey, maxCount), null, "");
        box.SetButtonPreset(1);
        box.Callback = callback;
        box.EnterAllowed = false;
        for (int i = 0; i < packages.Count; i++)
        {
            var pkg = packages[i];
            box.Values[$"autoActionMinute{i}"] = pkg.DurationMinutes;
            box.Values[$"autoActionMerat{i}"] = pkg.Merat;
            box.Values[$"autoActionMeso{i}"] = pkg.Meso;
        }
        box.Values["firstUseRedMerat"] = firstUseRedMerat;
        box.Values["actionName"] = Common(actionNameKey);
        box.Values["saleRate"] = saleRate;
        box.Values["packageCount"] = packages.Count;
        ShowBox?.Invoke(box);
    }

    /// <summary>Auto-fishing extend confirm callback (@0x140fa62d0, uisystemshopdialog.cpp:438).</summary>
    bool OnConfirmAutoFishing(int button, Hud.MessageBoxData box)
    {
        if (button != 0 || Model == null || !Model.HasLocalPlayer || !HasAutoFishingBuff()) return true;
        int pkg = ParsePackageIndex(box?.ClickedTag);
        SetAutoFishingCount?.Invoke((int)(Model.Tables.ExtendAutoFishMaxCount));
        SelectAutoFishingPackage?.Invoke(pkg);
        return true;
    }

    /// <summary>Auto-fishing cancel confirm callback (@0x140fa64a0, uisystemshopdialog.cpp:460).</summary>
    bool OnCancelAutoFishing(int button, Hud.MessageBoxData box)
    {
        if (button != 0) return true;
        SetAutoFishingCount?.Invoke(0);
        return true;
    }

    /// <summary>Auto-instrument extend confirm callback (@0x140fa67a0, uisystemshopdialog.cpp:496).</summary>
    bool OnConfirmAutoInstrument(int button, Hud.MessageBoxData box)
    {
        if (button != 0 || Model == null || !Model.HasLocalPlayer || !CanPlayEnsemble()) return true;
        int pkg = ParsePackageIndex(box?.ClickedTag);
        SetAutoInstrumentCount?.Invoke(Model.Tables.ExtendAutoPlayInstrumentMaxCount);
        SelectAutoInstrumentPackage?.Invoke(pkg);
        return true;
    }

    /// <summary>Auto-instrument cancel confirm callback (@0x140fa6970, uisystemshopdialog.cpp:518).</summary>
    bool OnCancelAutoInstrument(int button, Hud.MessageBoxData box)
    {
        if (button != 0) return true;
        SetAutoInstrumentCount?.Invoke(0);
        return true;
    }

    static int ParsePackageIndex(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return -1;
        var parts = tag.Split(',');
        return parts.Length > 1 ? Hud.MessageBoxData.WToI(parts[1]) - 1 : -1;
    }

    /// <summary>UISystemShopDialog::vfunc_49 (@0x140fa59b0, uisystemshopdialog.cpp:369): auto-action remaining count tooltip.</summary>
    public override IDictionary<string, object> GetTooltip(object[] args)
    {
        string text = Mode switch
        {
            4 => Common("s_system_shop_autofishing_icon_tooltip", GetAutoFishingCount()),
            5 => Common("s_system_shop_play_instrument_icon_tooltip", GetAutoInstrumentCount()),
            _ => "",
        };
        return string.IsNullOrEmpty(text) ? null : AsTooltip.Text(text);
    }

    /// <summary>UISystemShopDialog::OnNotifyChangeBuff (@0x140fa69b0, uisystemshopdialog.cpp:528).</summary>
    public void OnNotifyChangeBuff(int buffId, int eventBuffType)
    {
        if (buffId != 0 && buffId == AutoFishingBuffId()) RefreshFishing();
        else if (eventBuffType == 1) RefreshFishing();
        else if (eventBuffType == 4) RefreshInstrument();
    }

    /// <summary>UISystemShopDialog::OnBaitRemoved (@0x140fa6ab0, uisystemshopdialog.cpp:557).</summary>
    public void OnBaitRemoved()
    {
        BaitItem = null;
        SetBaitId?.Invoke(0);
        Host?.Push(TypeName);
    }

    /// <summary>UISystemShopDialog::OnBaitDropped (@0x140fa6ae0, uisystemshopdialog.cpp:564): requires stringTag 100
    /// (fishing bait) and CanUseBait.</summary>
    public bool OnBaitDropped(Item item)
    {
        if (item?.Data == null) return false;
        if (ItemTags.ByName(item.Data.StringTag) != 100) return false;
        if (!CanUseBait(item)) return false;
        BaitItem = item;
        SetBaitId?.Invoke(item.ItemId);
        Host?.Push(TypeName);
        return true;
    }

    /// <summary>Bait slot data binding ("SystemShopDialog", flags 0x11, uisystemshopdialog.cpp:412/:417).</summary>
    public override object GetBinding(string name)
    {
        if (!string.Equals(name, TypeName, StringComparison.Ordinal) || BaitItem == null || Model == null) return null;
        var slot = new List<(string, object)>();
        if (!ItemSlot.Build(slot, BaitItem, Model.HasLocalPlayer, 0x11, Model, Model.ItemSlotSources)) return null;
        var dict = new Dictionary<string, object>(slot.Count);
        foreach (var (k, v) in slot) dict[k] = v;
        return dict;
    }

    static int ToInt(object v) => v switch
    {
        int i => i, uint u => (int)u, double d => (int)d,
        _ => Convert.ToInt32(v, CultureInfo.InvariantCulture),
    };
}
