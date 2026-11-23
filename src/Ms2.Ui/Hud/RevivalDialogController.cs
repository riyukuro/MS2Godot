using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>The field the dialog reads (CFieldDataCli): the revival return field, the death penalty, the no-revive-here and infinite merat flags.</summary>
public sealed record RevivalField(int ReturnId, int DeathPenalty, bool DoNotRevivalHere, bool InfiniteMeratRevival);

/// <summary>The dungeon room's revival limits (DungeonRoomTable::GetRoom): m_limitMesoRevival, m_limitMeratRevival, m_defaultRevivalLimitCount.</summary>
public sealed record RevivalRoom(bool LimitMesoRevival, bool LimitMeratRevival, int DefaultRevivalLimitCount);

/// <summary>
/// What UIRevivalDialog reads from the rest of the client (the local player, the field, the dungeon room, the constants and the
/// platform host) and the places it writes to. Every member is one read or call of uirevivaldialog.cpp; the cite is on it.
/// </summary>
public sealed class RevivalEnvironment
{
    // ---- the local player (MyPC) ----
    public Func<bool> HasPlayer = () => true;
    /// <summary>MyPC::IsDead (SendRevivalMode1, uirevivaldialog.cpp:232).</summary>
    public Func<bool> IsDead = () => true;
    /// <summary>GetJobSystem()->GetJobGroupID() == 1 (SetDeathMessage, uirevivaldialog.cpp:329).</summary>
    public Func<bool> IsFirstJobGroup = () => false;
    /// <summary>PC::IsRevivalBlocked @0x141661970: a revival end tick or the Revival flag is set, or the buffs disable it here.</summary>
    public Func<bool> RevivalBlocked = () => false;
    /// <summary>PC::vfunc_78: the player's level, the first argument of CLua::CalcRevivalMerat / CalcRevivalMeso.</summary>
    public Func<int> Level = () => 1;
    /// <summary>FUN_1416749a0()->IsDisableRevivalHere (FUN_141abdab0, uirevivaldialog.cpp:813).</summary>
    public Func<bool> BuffsDisableRevival = () => false;
    /// <summary>FUN_1416749a0()->GetOverlapCount(m_51d8 constant, 0) (RefreshMeratButton, uirevivaldialog.cpp:686): the stacked revival debuff.</summary>
    public Func<int> RevivalOverlapCount = () => 0;
    /// <summary>FUN_1416749a0()->FUN_140970020(m_51d8 constant, 0, 0, 1) (uirevivaldialog.cpp:460, :532): the buff that stops a paid revive is on.</summary>
    public Func<bool> PaidReviveStopped = () => false;
    /// <summary>CEquipment::GetAbilityRate(SAType 0x42) and (0xa4): the merat and meso cost reductions.</summary>
    public Func<float> MeratAbilityRate = () => 0f, MesoAbilityRate = () => 0f;
    /// <summary>MyPC::GetMesoRevivalRemainingCount @0x141528160: max(daily max - used, 0).</summary>
    public Func<int> MesoRevivalRemaining = () => 0;
    /// <summary>CGameRule::FUN_140477d10(FUN_141658f30()) (RefreshMesoButton, uirevivaldialog.cpp:721): 1 = the meso revive is a flat rule.</summary>
    public Func<int> MesoRevivalRule = () => 0;
    /// <summary>False when the inputs of a paid revive's send are not available (the buffs' disable check, the meso revivals left, the
    /// platform's merat check): the paid buttons then send nothing. RevivalWiring sets it true with RevivalPaidInputs; the default is true.</summary>
    public Func<bool> PaidRevivalWired = () => true;

    // ---- the world, the field and the dungeon ----
    public Func<int> FieldId = () => 0;
    /// <summary>CStringTable::FUN_141811980(3, id): the map name of a field, for the 0x27d line and the 0x287 box
    /// (uirevivaldialog.cpp:363, :418).</summary>
    public Func<int, string> FieldName = id => id.ToString();
    public Func<RevivalField> Field = () => null;
    /// <summary>WorldService::FUN_141bdb660(8) (FUN_141abcb90, uirevivaldialog.cpp:637): no paid revive at all.</summary>
    public Func<bool> PaidRevivalOffByWorld = () => false;
    /// <summary>CFieldData::FUN_140374410(field id): the field's mapType is 3 (a home), where the meso revive costs nothing.</summary>
    public Func<int, bool> FieldRevivesForFree = _ => false;
    /// <summary>DAT_143ab2884 + DungeonRoomTable::GetRoom: the dungeon room (null outside one).</summary>
    public Func<RevivalRoom> Room = () => null;
    /// <summary>DAT_143ab2950: the revivals used in the room.</summary>
    public Func<int> RoomRevivalsUsed = () => 0;
    /// <summary>GetDungeonName.</summary>
    public Func<string> DungeonName = () => "";
    /// <summary>FeatureManager::IsFeatureEnabled(0x5d) (FUN_141abc7f0, uirevivaldialog.cpp:574).</summary>
    public Func<bool> RoomLimitFeature = () => false;

    // ---- constants, wallet, items ----
    /// <summary>Constants +0xa278 (FUN_141abc930, uirevivaldialog.cpp:591): merat revival switched off.</summary>
    public Func<bool> MeratRevivalOff = () => false;
    /// <summary>Constants +0x8cc0 / +0x90f8: the ceilings of the merat and meso cost reductions.</summary>
    public Func<float> MeratRateCap = () => 0f, MesoRateCap = () => 0f;
    /// <summary>Constants +0xcfe0: the seconds the leave-in-dungeon box counts down.</summary>
    public Func<int> LeaveBoxSeconds = () => 0;
    /// <summary>DAT_143ab2898: the meso the player holds (FUN_141abcad0, uirevivaldialog.cpp:618).</summary>
    public Func<long> Meso = () => 0;
    /// <summary>The platform's merat check (IClientPlatform vfunc_40(cost)); vfunc_41 is the same check for the feature the CN build tests.</summary>
    public Func<long, bool> CanPayMerat = _ => true;
    /// <summary>Unk_Inventory::FindItemByTag(4): the revival coupon item is held (FUN_141abd640).</summary>
    public Func<bool> HasCoupon = () => false;

    // ---- the outputs ----
    public Action<IOutPacket> Send = _ => { };
    /// <summary>FUN_141275260(id, 1, 1): the system notice of a common string id.</summary>
    public Action<int> NoticeById = _ => { };
    /// <summary>ShowSystemNotice(text, 1, 1).</summary>
    public Action<string> Notice = _ => { };
    /// <summary>CUIService FUN_141add1f0 (uirevivaldialog.cpp:455): the merat is short, the platform opens its payment.</summary>
    public Action PaymentShort = () => { };
    /// <summary>FUN_14151f200: show the next queued message box.</summary>
    public Action NextBox = () => { };
    /// <summary>FUN_140d73c60(box).</summary>
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;
    /// <summary>A box's tag change on the shown box (ShowOrReplaceByTag).</summary>
    public Action<MessageBoxData> ReplaceBox = MessageBoxQueue.Show;
}

/// <summary>
/// UIRevivalDialog (ms2/client/ms2/uirevivaldialog.cpp; UI id 0x4e38, movie uirevivaldialog), the death state's dialog: the
/// two buttons the player picks the way back with. Button 1 is the free revive at the return point (or leaving a dungeon whose
/// revive count is used up), button 2 the paid one: merat (mode 1) or meso (mode 2), picked by <see cref="PaidMode"/>. The
/// coupon check box (m_coupon) pays with the revival coupon instead. Costs follow CLua::CalcRevivalMerat / CalcRevivalMeso
/// (the client's precompiled Lua, read with luac): merat = 10 * overlap + 20, meso = 10000 when the rule flag is 1, else
/// 10000 + 1000 * max(level - 10, 0), each scaled by one minus the ability rate (at most its constant ceiling).
/// STILL-OPEN: ScaleValue's rounding (cut to a long here), the CN-build texts and platform checks, and the movie's own
/// AS3 API (setData, setButtonLabel, the btn1 / btn2 labels), which is called as the client calls it.
/// </summary>
public sealed class RevivalDialogController : UiController, IMessageBoxListener, IMessageBoxTimerListener
{
    public override string Movie => "uirevivaldialog";
    protected override string EntityMethod => "alignment";                  // the movie's game entity, as UIRevivalDialog::vfunc_43 finds it
    public override string TypeName => "RevivalDialog";                     // GetTypeName (uirevivaldialog.cpp:172)

    /// <summary>The ctor's SetFlag(0, 1) (uirevivaldialog.cpp:163): an always-on dialog (vfunc_56), so it never counts as a
    /// shown dialog in the guide's gate and is not closable (CUIService::IsDialogClosable).</summary>
    public override bool AlwaysOn => true;

    public RevivalEnvironment Env { get; } = new();

    /// <summary>vfunc_43 (@0x141abab30, uirevivaldialog.cpp:250-254): UIDialog's is not called; only the entity's vfunc_25
    /// (the alignment call) runs, so a saved place never keeps the dialog where it was.</summary>
    public override void Layout(double stageW, double stageH)
    {
        if (Host.Player.FindOwner("alignment") is { } al) Host.Invoke(al, "alignment");
    }

    /// <summary>The string tables: the common table by id and by key (the model's tables unless a host sets them).</summary>
    public Func<int, string> CommonById;
    public Func<string, string> CommonByKey;
    string Id(int id) => (CommonById ?? (i => Model?.Tables.Common(i)))(id) ?? "";
    string Key(string key) => (CommonByKey ?? (k => Model?.Tables.Common(k)))(key) ?? "";

    /// <summary>m_1b4: 0 = no paid revive, 1 = merat, 2 = meso (FUN_141abcb90 @0x141abcb90).</summary>
    public int PaidMode { get; private set; }
    /// <summary>m_190 (merat cost, -1 until computed), m_198 (meso cost), m_1a0 / m_1a4 (the meso count and rule it was made with).</summary>
    public long MeratCost { get; private set; } = -1;
    public long MesoCost { get; private set; }
    int mesoRemaining, mesoRule;
    /// <summary>m_1b0: the first button leaves the dungeon (its revival count is used up).</summary>
    public bool FirstButtonLeaves { get; private set; }
    /// <summary>m_178: the dialog ignores the buttons while it is above 0 (the ctor / load set -1).</summary>
    int buttonLock = -1;
    /// <summary>m_180: the merat / meso confirmation box, kept so a new cost can replace its text.</summary>
    public MessageBoxData ConfirmBox { get; private set; }
    /// <summary>The coupon check box state (m_coupon): enabled and selected.</summary>
    public bool CouponEnabled { get; private set; }
    public bool CouponSelected { get; private set; }
    /// <summary>The labels the movie got (setButtonLabel 1 / 2) and the death message (setData).</summary>
    public string Label1 { get; private set; } = "";
    public string Label2 { get; private set; } = "";
    public string DeathMessage { get; private set; } = "";
    /// <summary>Ticks of the 1000 ms interval timer (m_1a8) are driven by <see cref="Update"/>.</summary>
    uint lastCheck;

    /// <summary>OnLoadComplete (@0x141aba850): UIDialog's, m_178 -1, the buttons, the death message.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        Refresh();
    }

    /// <summary>The state half of OnLoadComplete: UpdateButtons, SetDeathMessage (the platform's vfunc_43(0xe, 0, 1000) is not ported).</summary>
    public void Refresh()
    {
        buttonLock = -1;
        UpdateButtons();
        SetDeathMessage();
    }

    /// <summary>vfunc_67 (@0x141aba8c0): UIDialog's close, then the confirmation box and the merat cost forgotten.</summary>
    public override void Close()
    {
        base.Close();
        ConfirmBox = null;
        MeratCost = -1;
        MesoCost = 0;
    }

    /// <summary>vfunc_28 (@0x141abac00): "onRevival(button)" is the movie's button click.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (!string.Equals(name, "onRevival", StringComparison.OrdinalIgnoreCase)) return false;
        OnRevival(args is { Length: > 0 } ? Convert.ToInt32(args[0]) : 0);
        return true;
    }

    /// <summary>vfunc_72 (@0x141abace0): every 1000 ms the paid button is recomputed (the debuff stack and the ability rate change).</summary>
    public void Update(uint nowMs)
    {
        if (nowMs - lastCheck < 1000) return;
        lastCheck = nowMs;
        RefreshPaidButton();
    }

    /// <summary>FUN_141abad30 (@0x141abad30): button 1 = the free revive, button 2 = the paid one for the mode.</summary>
    public void OnRevival(int button)
    {
        if (buttonLock > 0) return;
        switch (button)
        {
            case 1: FreeRevive(); break;
            case 2:
                if (PaidMode == 2) MesoRevive();
                else if (PaidMode == 1) MeratRevive();
                break;
        }
    }

    // ---- the message box answers ----

    /// <summary>OnMessage (@0x141aba920): the "leaveInField" box answered 0 shows the next queued box.</summary>
    public bool OnMessageBox(MessageBoxController sender, int button, string tag)
    {
        if (button == 0 && tag == "leaveInField") Env.NextBox();
        return false;
    }

    /// <summary>The leave-the-dungeon answer (UI message 0x7d2, uirevivaldialog.cpp:213-216): RequestMoveField mode 1.</summary>
    public void OnLeaveDungeon() => Env.Send(new RequestMoveFieldLeave());

    /// <summary>OnMessage 0x7d2 (@0x141aba920): the leave box's countdown ended; any box of this listener leaves.</summary>
    public void OnTimerComplete(MessageBoxController sender, string extra) => OnLeaveDungeon();

    /// <summary>SendRevivalMode0 (@0x141abadb0): the answer 0 of the return-point box sends mode 0 and shows the next box.</summary>
    bool SendMode0(int result, MessageBoxData box)
    {
        if (result == 0)
        {
            Env.Send(new RevivalRequest(0));
            Env.NextBox();
        }
        return true;
    }

    /// <summary>SendRevivalMode1 (@0x141abaa30): the answer 0 of the merat / meso box sends [1, coupon] while dead, else notice 0x284.</summary>
    bool SendMode1(int result, MessageBoxData box)
    {
        bool coupon = CouponChecked();
        if (result == 0)
        {
            if (!Env.HasPlayer()) return false;
            if (Env.IsDead()) Env.Send(new RevivalRequest(1, coupon));
            else Env.NoticeById(0x284);
        }
        ConfirmBox = null;
        MeratCost = -1;
        return true;
    }

    // ---- the buttons ----

    /// <summary>FUN_141abb600 (@0x141abb600): the first button.</summary>
    void FreeRevive()
    {
        if (FirstButtonLeaves)
        {
            if (Env.Field() == null) return;
            var box = MessageBoxQueue.Create(0, ClientFormat.Format(Key("s_room_exit_msg_default_revival_limit_dungeon"), Env.DungeonName()), this, "leaveInField");
            box.SetButtonLabelIds(0x476, 0, 0);
            box.SetTimer(Env.LeaveBoxSeconds(), 1, Id(0x2b8));
            Env.ShowBox(box);
            return;
        }
        var field = Env.Field();
        if (field == null) return;
        int fieldId = Env.FieldId();
        int to = field.ReturnId != 0 ? field.ReturnId : fieldId;
        if (fieldId != to)
        {
            var box = MessageBoxQueue.CreateWithCallback(ClientFormat.Format(Id(0x287), ReturnFieldName(to)), 1, SendMode0);
            box.SetButtonPreset(2);
            Env.ShowBox(box);
        }
        else Env.Send(new RevivalRequest(0));
    }

    string ReturnFieldName(int id) => Env.FieldName(id) ?? "";

    /// <summary>FUN_141abbb20 (@0x141abbb20): the merat button.</summary>
    void MeratRevive()
    {
        if (!Env.HasPlayer() || !Env.PaidRevivalWired()) return;
        var field = Env.Field();
        if (field == null || !CanReviveHere(field)) return;
        RefreshMeratButton();
        bool coupon = CouponChecked();
        bool canPay = CanPayMerat(MeratCost);
        bool hasCoupon = coupon && Env.HasCoupon();
        if (!hasCoupon && !canPay) { Env.PaymentShort(); return; }
        if (!field.InfiniteMeratRevival && Env.PaidReviveStopped()) { Env.NoticeById(0x281); return; }
        if (coupon) { Env.Send(new RevivalRequest(1, true)); return; }
        ConfirmBox = MessageBoxQueue.CreateWithCallback(ClientFormat.Format(Id(0x283), MeratCost), 1, SendMode1);
        ConfirmBox.Extra = "revival_merat";             // FUN_141a01540 (the box tag)
        Env.ShowBox(ConfirmBox);
    }

    /// <summary>FUN_141abc080 (@0x141abc080): the meso button.</summary>
    void MesoRevive()
    {
        if (!Env.HasPlayer() || !Env.PaidRevivalWired()) return;
        var field = Env.Field();
        if (field == null || !CanReviveHere(field)) return;
        RefreshMesoButton();
        bool coupon = CouponChecked();
        bool hasCoupon = coupon && Env.HasCoupon();
        if (!hasCoupon && Env.Meso() < MesoCost)
        {
            long cost = Scale(CalcMerat(Env.Level(), 0), 1f - Math.Min(Env.MeratRateCap(), Env.MeratAbilityRate()));
            if (cost < 0) cost = 0;
            if (!MeratRevivalAllowed() || !Env.CanPayMerat(cost)) { Env.NoticeById(0x34); return; }
            ConfirmBox = MessageBoxQueue.CreateWithCallback(ClientFormat.Format(Key("s_msg_revival_meso_cannot_merat_msg_box"), cost), 1, SendMode1);
            ConfirmBox.Extra = "revival_merat";             // FUN_141a01540 (the box tag)
            Env.ShowBox(ConfirmBox);
            return;
        }
        if (!field.InfiniteMeratRevival && Env.PaidReviveStopped()) { Env.NoticeById(0x281); return; }
        Env.Send(new RevivalRequest(2, coupon));
    }

    /// <summary>FUN_141abdab0 (@0x141abdab0): the field and the buffs allow a revive here.</summary>
    bool CanReviveHere(RevivalField field)
    {
        if (field.DoNotRevivalHere) { Env.NoticeById(0x282); return false; }
        if (!Env.HasPlayer()) return false;
        if (Env.BuffsDisableRevival()) { Env.Notice(Key("s_msg_revival_merat_cannot_here_debuff")); return false; }
        return true;
    }

    bool CouponChecked() => CouponEnabled && CouponSelected;

    // ---- the mode and the costs ----

    /// <summary>FUN_141abcb90 (@0x141abcb90): the paid mode: 0 off by the world, 2 meso, 1 merat.
    /// Without the paid inputs wired (PaidRevivalWired false) the mode is 0, the client's own one-button state ("btn1",
    /// s_msg_die_none; m_1b4 == 0, uirevivaldialog.cpp:385, :646-649): the client never shows a paid button that does nothing.</summary>
    public void PickMode()
    {
        if (Env.PaidRevivalOffByWorld() || !Env.PaidRevivalWired()) PaidMode = 0;
        else if (Env.FieldRevivesForFree(Env.FieldId())) PaidMode = 2;
        else if (MesoRevivalAllowed()) PaidMode = 2;
        else PaidMode = MeratRevivalAllowed() ? 1 : 0;
    }

    /// <summary>FUN_141abc7f0 (@0x141abc7f0): the meso revive is a flat rule or has a daily count left, and the room does not forbid it.</summary>
    bool MesoRevivalAllowed()
    {
        if (!Env.HasPlayer()) return false;
        bool ok = Env.MesoRevivalRule() == 1 || Env.MesoRevivalRemaining() > 0;
        if (ok && Env.RoomLimitFeature() && Env.Room() is { LimitMesoRevival: true }) ok = false;
        return ok;
    }

    /// <summary>FUN_141abc930 (@0x141abc930): the merat revive is on and the room does not forbid it.</summary>
    bool MeratRevivalAllowed()
    {
        if (Env.MeratRevivalOff()) return false;
        return Env.Room() is not { LimitMeratRevival: true };
    }

    bool CanPayMerat(long cost) => Env.CanPayMerat(cost);

    /// <summary>CLua::CalcRevivalMerat(level, overlap): 10 * overlap + 20.</summary>
    public static long CalcMerat(int level, int overlap) => 10L * overlap + 20;

    /// <summary>CLua::CalcRevivalMeso(level, count, rule): 10000 when the rule is 1, else 10000 + 1000 * max(level - 10, 0).</summary>
    public static long CalcMeso(int level, int count, int rule) => rule == 1 ? 10000 : 10000 + 1000L * Math.Max(level - 10, 0);

    static long Scale(long value, float factor) => (long)(value * (double)factor);

    /// <summary>RefreshMeratButton (@0x141abccb0): the cost, and on a change the death text, the button label and the open box text.</summary>
    void RefreshMeratButton()
    {
        if (!Env.HasPlayer()) return;
        long cost = Scale(CalcMerat(Env.Level(), Env.RevivalOverlapCount()), 1f - Math.Min(Env.MeratRateCap(), Env.MeratAbilityRate()));
        if (cost < 0) cost = 0;
        if (MeratCost == cost) return;
        SetDeathMessage();
        SetLabel(2, ClientFormat.Format(Id(0x280), cost));
        MeratCost = cost;
        if (ConfirmBox != null)
        {
            ConfirmBox.Msg = ClientFormat.Format(Id(0x283), MeratCost);
            Env.ReplaceBox(ConfirmBox);
        }
    }

    /// <summary>RefreshMesoButton (@0x141abd290).</summary>
    void RefreshMesoButton()
    {
        if (!Env.HasPlayer()) return;
        int count = Env.MesoRevivalRemaining();
        int rule = Env.MesoRevivalRule();
        long cost = Scale(CalcMeso(Env.Level(), count, rule), 1f - Math.Min(Env.MesoRateCap(), Env.MesoAbilityRate()));
        if (cost < 0) cost = 0;
        if (Env.FieldRevivesForFree(Env.FieldId())) cost = 0;
        SetDeathMessage();
        SetLabel(2, rule != 0 ? ClientFormat.Format(Id(0x286), cost) : ClientFormat.Format(Id(0x285), cost, count));
        MesoCost = cost;
        mesoRemaining = count;
        mesoRule = rule;
    }

    void RefreshPaidButton()
    {
        if (PaidMode == 1) RefreshMeratButton();
        else if (PaidMode == 2) RefreshMesoButton();
        UpdateCoupon();
    }

    /// <summary>UpdateButtons (@0x141abb420): the first label, the mode, the movie's label frame, the paid button.</summary>
    void UpdateButtons()
    {
        if (!Env.HasPlayer()) return;
        SetLabel(1, FirstButtonLabel());
        PickMode();
        if (Host?.Entity != null) Host.Invoke(Host.Entity, "gotoAndStop", PaidMode == 0 ? "btn1" : "btn2");   // vfunc_13 (ASGameEntity goto label)
        RefreshPaidButton();
    }

    /// <summary>GetFirstButtonLabel (@0x141abd790): "revive here" (0x27e), or the dungeon's revivals left, or "leave the dungeon".</summary>
    string FirstButtonLabel()
    {
        FirstButtonLeaves = false;
        string label = Id(0x27e);
        if (!Env.HasPlayer() || Env.Room() is not { DefaultRevivalLimitCount: > 0 } room) return label;
        uint left = (uint)room.DefaultRevivalLimitCount - (uint)Env.RoomRevivalsUsed();     // nLeft is unsigned (uirevivaldialog.cpp:787): used above the limit wraps
        if (left == 0) { FirstButtonLeaves = true; return Key("s_room_exit_dungeon"); }
        return ClientFormat.Format(Key("s_msg_revival_count_btn"), left);
    }

    /// <summary>SetDeathMessage (@0x141abae20): the text by the player, the field and the mode, and "the return field" line.</summary>
    public void SetDeathMessage()
    {
        if (!Env.HasPlayer()) return;
        var field = Env.Field();
        string msg;
        if (Env.IsFirstJobGroup() || (field != null && field.DeathPenalty == 0)) msg = Id(0x27c);
        else
        {
            bool repeat = Env.RevivalBlocked();
            string suffix = PaidMode == 2 ? "meso" : PaidMode == 1 ? "merat" : "none";
            msg = Key((repeat ? "s_msg_die_repeat_" : "s_msg_die_") + suffix);
        }
        if (field != null && field.ReturnId != Env.FieldId())
            msg += "\n" + ClientFormat.Format(Id(0x27d), ReturnFieldName(field.ReturnId));
        DeathMessage = msg;
        if (Host?.Entity != null) Host.Invoke(Host.Entity, "setData", msg, Env.RevivalBlocked() ? 1 : 0);
    }

    /// <summary>FUN_141abd640 (@0x141abd640): the coupon check box is on and enabled only with a paid mode and the coupon item.</summary>
    void UpdateCoupon()
    {
        if (PaidMode != 0 && Env.HasCoupon())
        {
            if (!CouponEnabled) { CouponSelected = true; CouponEnabled = true; }
        }
        else { CouponSelected = false; CouponEnabled = false; }
    }

    /// <summary>The check box's click (ASCheckBox::SetSelected on m_coupon).</summary>
    public void SetCouponSelected(bool selected) { if (CouponEnabled) CouponSelected = selected; }

    void SetLabel(int button, string text)
    {
        if (button == 1) Label1 = text; else Label2 = text;
        if (Host?.Entity != null) Host.Invoke(Host.Entity, "setButtonLabel", button, text);
    }
}
