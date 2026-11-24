using Godot;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Net;

// The air taxi: CCashCallManager::FUN_140b030b0 and StateCashCall (0x2a) of the own pc.
public partial class LiveSession
{
    CashCallState cashCall;

    /// <summary>GetCurStateId() is 0x2a: an air taxi call runs.</summary>
    public bool CashCalling => cashCall is { Running: true };

    /// <summary>
    /// The facts of <see cref="CashCall.Start"/> as far as the port knows them.
    /// STILL-OPEN: FUN_141527160(0x2a)'s own gates (taken as "no taxi or cash call runs", as the taxi's are); a FreeAirTaxi game event
    /// (CGameRule::AddFreeAirTaxiEvent, gameevent.cpp:1065) is read but not kept, so no free air taxi runs; GetMeratBalance and the JP
    /// region (not this data's).
    /// </summary>
    CashCallFacts CashCallFactsNow(int field)
    {
        long now = ServerTime;
        var items = BagItemsByTag(now);
        bool has = TaxiPlayerReady;
        return new CashCallFacts
        {
            HasPlayer = has,
            Dead = Me.Dead,
            Riding = has && player.Mount != null,
            StateId = OwnCurStateId(OwnSkillStateRunsOf?.Invoke() ?? false),
            CanEnterCashCall = !TaxiCalling && !CashCalling,
            Visited = Ms2Godot.Ui.StubHudData.Instance.VisitedFields.Contains(field),
            Vip = Me.PremiumTime > now,                  // PC::IsVip (pc.cpp:2209); m_n1198 is the fitting doll's
            VipAirTaxiFeature = CashCallFeature?.Invoke(CashCall.VipAirTaxiFeature) ?? false,
            MeratAirTaxiFeature = CashCallFeature?.Invoke(CashCall.MeratAirTaxiFeature) ?? false,
            AirTaxiUid = RecallChecks.FirstByTag(TagRows(items), CashCall.AirTaxiTag),
            AdvancedUid = RecallChecks.FirstByTag(TagRows(items), CashCall.AdvancedAirTaxiTag),
            AdvancedTickets = CashCall.CountByTag(items, CashCall.AdvancedAirTaxiTag),
            MeratAirTaxiPrice = Constants()?.Int("MeratAirTaxiPrice", (int)CashCall.DefaultMeratAirTaxiPrice) ?? CashCall.DefaultMeratAirTaxiPrice,
        };
    }

    /// <summary>CFeatureManager::IsFeatureEnabled for the cash call (the HUD sets it).</summary>
    public Func<int, bool> CashCallFeature;

    /// <summary>g_143ab2a90.FUN_140a5f7b0(tag, 0) over the bag (<see cref="CashCall.CountByTag"/>).</summary>
    public int TicketCount(int tag) => CashCall.CountByTag(BagItemsByTag(ServerTime), tag);

    /// <summary>The bag's items as FindItemByTag and FUN_140a5f7b0 read them: uid, expiry, PC::CanUseItem as far as the port decides it,
    /// the data's string tag and the amount.</summary>
    List<(long, long, bool, int, int)> BagItemsByTag(long now)
    {
        var list = new List<(long, long, bool, int, int)>();
        if (ItemDataOf(0) == null && itemData == null) return list;
        foreach (var e in Items.All)
        {
            var data = itemData.Get(e.Id);
            if (data == null) continue;
            list.Add((e.Uid, e.ExpiryTime, Ms2.Ui.Items.InventoryCount.CanUse(e, itemData, CharacterId, now),
                Ms2.Ui.Items.ItemTags.ByName(data.StringTag), e.Amount));
        }
        return list;
    }

    static IEnumerable<(long, long, bool, int)> TagRows(List<(long Uid, long Expiry, bool Usable, int Tag, int Amount)> items)
    {
        foreach (var i in items) yield return (i.Uid, i.Expiry, i.Usable, i.Tag);
    }

    /// <summary>CCashCallManager::FUN_140b030b0(field) (the world map's TakeCashTaxi): the call, the ride off before it, or nothing.</summary>
    public bool StartCashCall(int field)
    {
        var f = CashCallFactsNow(field);
        var outcome = CashCall.Start(f, out int kind, out long uid);
        if (LiveCheck) GD.Print($"live: cash call to {field}: {outcome} kind {kind} item {uid}");
        switch (outcome)
        {
            case CashCall.Outcome.Call:
                BeginCashCall(kind, uid, field);
                return true;
            case CashCall.Outcome.RideOff:
                // SendRequestRideMode1(RideOffActionCashCall(0) with m_n28 = field, m_n2c = kind, m_n30 = item); the answer starts the state
                var ride = player?.Mount?.Data;
                if (RideOff.Request(true, ride != null, ride?.Type ?? 0, ride?.EnableRideOffUI ?? false, 0, false,
                        new RideOffCashCallRequest(field, (byte)kind, uid), Send) == RideOff.Outcome.CantRide)
                    SystemNoticeSink?.Invoke(MessageBoxData.Common(RideOff.CantRideNoticeId));
                return true;
            default:
                return false;
        }
    }

    /// <summary>vfunc_59(0x2a, new StateCashCallData(kind, item, field)) for the own pc.</summary>
    void BeginCashCall(int kind, long uid, int field)
    {
        var k = Constants();
        int ms = CashCallState.Duration(kind, (key, def) => k?.Int(key, def) ?? def, true);
        cashCall = new CashCallState(new CashCallHost(this));
        cashCallStateAtBegin = Me.State;
        cashCall.Begin(kind, uid, field, ms);
    }
    int cashCallStateAtBegin;

    /// <summary>MyPC::vfunc_121's case 4 (mypc.cpp:3420-3425): a ride off answered with a RideOffActionCashCall starts StateCashCall
    /// with its kind, item and field.</summary>
    void CashCallAfterRideOff(RideOffAction action)
    {
        if (action is RideOffCashCall c) BeginCashCall(c.Value2C, c.Value30, c.Value28);
    }

    /// <summary>The call is no longer the own state: a server state other than the one it began in, death, a ride or a skill's cast
    /// (CStateManager::ChangeState runs its vfunc_5).</summary>
    bool CashCallStateLeft() =>
        selfObject < 0 || player == null || !IsInstanceValid(player) || Me.Dead || Me.State != cashCallStateAtBegin
        || player.Mount != null || player.DeathHoldsMove || player.SkillSequence != null;

    void TickCashCall(double delta)
    {
        if (cashCall == null) return;
        if (CashCallStateLeft()) { cashCall.LeaveState(); cashCall = null; return; }
        cashCall.Tick(delta);
        if (cashCall.Finished) cashCall = null;
    }

    bool CashCallHoldsMove(bool moveInput)
    {
        if (cashCall == null) return false;
        bool held = cashCall.HoldsMove(moveInput);
        if (cashCall.Finished) cashCall = null;
        return held;
    }

    /// <summary>The own pc goes (a field move): the state goes with it, nothing is sent and no notice shows.</summary>
    void DropCashCall() => cashCall = null;

    sealed class CashCallHost : ICashCallHost
    {
        readonly LiveSession s;
        public CashCallHost(LiveSession s) { this.s = s; }

        bool Own => s.player != null && IsInstanceValid(s.player);
        public void Send(IOutPacket packet) => s.Send(packet);

        /// <summary>FindItemByUid then FUN_140a63880(item, argument), as <see cref="RecallHost.UseItem"/> does with "".</summary>
        public bool UseItem(long uid, string argument)
        {
            var item = s.Items.Get(uid);
            if (item == null || s.Me.Dead) return false;
            var data = ItemDataOf(item.Id);
            if (item.ExpiryTime > 0 && item.ExpiryTime <= s.ServerTime && data?.HasMaidId != true) return false;
            s.Send(new Ms2.Net.ItemUseRequest(uid, argument ?? ""));
            return true;
        }

        public string ItemName(long uid)
        {
            var item = s.Items.Get(uid);
            if (item == null) return null;
            return Ms2.Ui.GameTables.Shared(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), GameOptions.StringLanguage).ItemName(item.Id) ?? "";
        }

        public void SystemNotice(string text) => s.SystemNoticeSink?.Invoke(text);
        public string Common(int id) => MessageBoxData.Common(id);

        public string CommonResource(string key)
        {
            try { return Ms2.Ui.GameTables.Shared(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), GameOptions.StringLanguage).CommonResource(key); }
            catch { return ""; }
        }

        public void StartCasting(string text, int durationMs) =>
            s.CastingBar?.StartCastingText(null, text ?? "", durationMs, showTime: false, activeBar: true, endText: "");

        public void CancelCasting() => s.CastingBar?.Cancel();

        public void PlayAnimation(string name)
        {
            if (Own) s.player.StartInteractAnim(name, false);
        }

        public void PlayIdle()
        {
            if (Own) s.player.StopInteractAnim();
        }

        public void SetShown(bool shown)
        {
            if (Own) s.player.Visible = shown;
        }

        /// <summary>STILL-OPEN: SpawnEffect puts the effect 150 behind the player (-UNIT_Y turned by its facing) and turns it; here it
        /// plays at the body.</summary>
        public void SpawnEffect(string path) => s.PlayOwnEffect(path);
    }
}
