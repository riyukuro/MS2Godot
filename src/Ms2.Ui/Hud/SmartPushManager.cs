using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>What the push classes ask the field (smartpush.cpp vfunc_6 of each class). A null answer function leaves that
/// question at "no": the push is not wanted.</summary>
public sealed class SmartPushField
{
    /// <summary>WorldService::GetLocalPlayer() != 0.</summary>
    public Func<bool> HasPlayer = () => false;
    /// <summary>MyPC::IsRiding().</summary>
    public Func<bool> IsRiding = () => false;
    /// <summary>GetRidee()->GetData()->m_basic.m_nType (1 and 2 are not wanted by the riding pushes).</summary>
    public Func<int> RideeType = () => 0;
    /// <summary>The player's buff list HasEnabledWithEventBuffType(type).</summary>
    public Func<int, bool> EventBuff = _ => false;
    /// <summary>CAdventureLevelTableHelper::IsReached(player level).</summary>
    public Func<bool> AdventureReached = () => false;
    /// <summary>FUN_140954150 != 0, the gate of the auto fishing push.</summary>
    public Func<bool> FishingAllowed = () => false;
    /// <summary>The instrument manager for AutoPlayInstrument::vfunc_6 (:1830): 0 not wanted, 1 wanted, 2 wanted and the
    /// ensemble can be played (the player is in state 0x2f, performing, and not a remote performer).</summary>
    public Func<int> Instrument = () => 0;
    /// <summary>The AutoInteraction classes' vfunc_6 (nearby gathering object, function cube): STILL-OPEN, the field's interact
    /// objects and the home cubes are not asked here.</summary>
    public Func<string, int> Interaction = _ => 0;
    /// <summary>IsFirstUseRedMerat(kind): the description with the red merat text.</summary>
    public Func<int, bool> FirstUseRedMerat = _ => false;
    /// <summary>CGameRule GetAutoFishingSale / GetAutoPlayInstrumentSale: the sale in hundredths of a percent, 0 = none.</summary>
    public Func<string, uint> Sale = _ => 0;
}

/// <summary>
/// CSmartPushManager (smartpushmanager.cpp) with the CSmartPush objects it keeps: one push per table row whose content has a
/// class (CreatePushes, :79), polled every 100 ms against the field (UpdatePushes @0x140dd8440) to add and remove the strip's
/// buttons, and answered through the base's click (vfunc_3, smartpush.cpp:285).
/// </summary>
public sealed class SmartPushManager
{
    public sealed class Push
    {
        public int Id;
        public int N1c;
        public int State;
        public SmartPushTable.Data Data;
        internal SmartPushNotices.PushClass Class;
    }

    readonly Func<string, string> common;
    readonly SmartPushNotices notices;
    readonly SmartPushField field;
    readonly Push[] pushes;
    readonly Dictionary<int, Push> byId = new();
    readonly SmartPushView view = new();
    uint lastTick;

    /// <summary>The strip: AddPush is false and RemovePush is false while it has no entity (uismartpush.cpp:122).</summary>
    public ISmartPushStrip Strip;
    /// <summary>The GameOption status DisplayHideSmartPush (m_bSuppress): every push is then unwanted.</summary>
    public Func<bool> Suppressed = () => false;

    public SmartPushManager(SmartPushTable table, SmartPushNotices notices, Func<string, string> commonByKey, SmartPushField field)
    {
        this.notices = notices;
        common = commonByKey;
        this.field = field ?? new SmartPushField();
        var list = new List<Push>();
        foreach (var d in (table ?? SmartPushTable.Empty).All)
        {
            var c = SmartPushNotices.ClassOf(d.Content);
            if (c == SmartPushNotices.PushClass.None) continue;                    // CreatePushes: no class, no push
            var p = new Push { Id = d.Id, Data = d, Class = c };
            list.Add(p);
            byId[d.Id] = p;
        }
        list.Sort((a, b) => a.Id.CompareTo(b.Id));       // the client walks a hash map; the order is not kept
        pushes = list.ToArray();
    }

    public int Count => pushes.Length;
    public Push Get(int id) => byId.TryGetValue(id, out var p) ? p : null;

    /// <summary>CSmartPush::vfunc_6 of the push's class.</summary>
    public int Wanted(Push p)
    {
        var f = field;
        switch (p.Data.Content)
        {
            case "SafeRiding":
                if (!RidesWater(f)) return 0;
                return f.EventBuff(2) ? 2 : 1;                                       // :2140
            case "SafeWaterRiding":
                if (!RidesWater(f) || f.AdventureReached()) return 0;                // :2600
                return f.EventBuff(3) ? 2 : 1;
            case "SafeWaterRidingToggle":
                if (!RidesWater(f) || !f.AdventureReached()) return 0;               // :2b30
                return f.EventBuff(3) ? 2 : 1;
            case "AutoFishing":
                if (!f.HasPlayer() || !f.FishingAllowed()) return 0;
                return f.EventBuff(1) ? 2 : 1;
            case "AutoPlayInstrument":
                return f.HasPlayer() ? f.Instrument() : 0;                           // :1830
        }
        return p.Class == SmartPushNotices.PushClass.AutoInteraction && f.HasPlayer() ? f.Interaction(p.Data.Content) : 0;
    }

    /// <summary>The player rides, on a ridee whose type is neither 1 nor 2 (the shared head of the riding pushes' vfunc_6).</summary>
    static bool RidesWater(SmartPushField f)
    {
        if (!f.HasPlayer() || !f.IsRiding()) return false;
        int t = f.RideeType();
        return t != 1 && t != 2;
    }

    /// <summary>UpdatePushes: without the strip's entity nothing happens; at most once per 100 ms each push is compared with
    /// what the field wants, and a change removes or adds its button. A failed add keeps the old state.</summary>
    public void Update(uint nowMs)
    {
        if (Strip == null || !Strip.Ready) return;
        if (nowMs - lastTick < 100) return;
        lastTick = nowMs;
        bool hide = Suppressed();
        for (int i = 0; i < pushes.Length; i++)
        {
            var p = pushes[i];
            int wanted = hide ? 0 : Wanted(p);
            if (p.State != wanted)
            {
                if (wanted == 0) { Strip.RemovePush(p.Id); p.State = 0; }
                else
                {
                    int old = p.State;
                    p.State = wanted;
                    if (!AddToStrip(p)) p.State = old;
                }
            }
        }
    }

    bool AddToStrip(Push p)
    {
        var v = view;
        v.Id = p.Id;
        v.Text = Text(p);
        v.Icon = p.Data.ImgPath;
        v.IsToggle = p.Data.Content == "SafeWaterRidingToggle";                      // vfunc_11
        v.Active = p.State == 2;
        v.IsSmallActiveIcon = p.Data.Content == "SafeRiding" && field.AdventureReached();   // :2470
        v.ActiveMovie = p.Data.Content is "AutoFishing" or "AutoPlayInstrument" ? "UISmartPush_autofish" : p.Data.ImgPath;
        v.EventPopup = EventPopup(p);
        return Strip.AddPush(v);
    }

    /// <summary>vfunc_7: the button's text by content and active state.</summary>
    string Text(Push p)
    {
        bool on = p.State == 2;
        string key = p.Data.Content switch
        {
            "AutoFishing" => on ? "s_smartpush_autofishing_active" : "s_smartpush_autofishing",
            "AutoPlayInstrument" => on ? "s_msg_auto_play_instrument_active" : "s_msg_auto_play_instrument",
            "SafeRiding" => on ? "s_smartpush_saferiding_active" : "s_smartpush_saferiding",
            "SafeWaterRiding" or "SafeWaterRidingToggle" => on ? "s_smartpush_saferiding_water_active" : "s_smartpush_saferiding_water",
            _ when p.Class == SmartPushNotices.PushClass.AutoInteraction => "s_mastery_" + p.Data.Content.ToLowerInvariant() + "_button",
            _ => null,
        };
        return key == null ? "" : common(key) ?? "";
    }

    /// <summary>vfunc_10 of the fishing and instrument pushes: the sale popup with the sale in percent, "" without one.</summary>
    string EventPopup(Push p)
    {
        if (p.Data.Content is not ("AutoFishing" or "AutoPlayInstrument")) return "";
        uint sale = field.Sale(p.Data.Content);
        return sale == 0 ? "" : ClientFormat.Format(common("s_smartpush_autoaction_sale_popup"), sale / 100);
    }

    /// <summary>vfunc_9: the confirmation text of the riding pushes (the red merat form when it is the first use).</summary>
    string Description(Push p)
    {
        switch (p.Data.Content)
        {
            case "SafeRiding":
                return ClientFormat.Format(common(field.FirstUseRedMerat(2) ? "s_smartpush_saferiding_rbmerat_desc" : "s_smartpush_saferiding_desc"), (long)p.Data.RequireMerat);
            case "SafeWaterRiding":
                return ClientFormat.Format(common(field.FirstUseRedMerat(3) ? "s_smartpush_saferiding_water_rbmerat_desc" : "s_smartpush_saferiding_water_desc"), (long)p.Data.RequireMerat);
        }
        return "";
    }

    /// <summary>OnPushClicked (:125) then the push's vfunc_3. The base (smartpush.cpp:285): a free push answers at once; one
    /// with a merat price asks first (a type 0x20 box, yes answers). The toggle (:2a80) always answers with accept false.
    /// STILL-OPEN: AutoFishing / AutoPlayInstrument / AutoInteraction override vfunc_3 with their package and item boxes
    /// (the fishing and instrument managers, the inventory and the mastery tables are not wired); they answer as the base.</summary>
    public void OnPushClicked(int id)
    {
        var p = Get(id);
        if (p == null) return;
        if (p.Class == SmartPushNotices.PushClass.Toggle || p.Data.RequireMerat == 0) { notices.Answer(p.Id, p.N1c, false); return; }
        var box = MessageBoxQueue.CreateWithCallback(Description(p), 0x20, (result, _) =>
        {
            if (result == 0) notices.Answer(p.Id, p.N1c, false);                       // FUN_140dcf800
            return true;
        });
        notices.ShowBox?.Invoke(box);
    }

    /// <summary>ResetPushStates: every push forgets that its button is shown (UISmartPush::vfunc_3), so the next update adds
    /// the wanted ones again.</summary>
    public void ResetPushStates()
    {
        for (int i = 0; i < pushes.Length; i++) pushes[i].State = 0;
    }
}
