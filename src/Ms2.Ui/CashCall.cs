using Ms2.Net.Packets;
using Ms2.Net.Wire;

namespace Ms2.Ui;

/// <summary>What CCashCallManager::FUN_140b030b0 (@0x140b030b0, cashcallmanager.cpp:65) reads of the player, the bag and the rules.</summary>
public struct CashCallFacts
{
    /// <summary>WorldService::GetLocalPlayer != null; IsDead; GetCurStateId; IsRiding (FUN_1416764d0).</summary>
    public bool HasPlayer, Dead, Riding;
    public int StateId;
    /// <summary>FUN_141527160(0x2a): the player can enter StateCashCall.</summary>
    public bool CanEnterCashCall;
    /// <summary>The destination is in DAT_143ab3058, the fields the player has been to.</summary>
    public bool Visited;
    /// <summary>PC::IsVip, CFeatureManager feature 0x57 (VIPAirTaxi), CGameRule::IsFreeAirTaxiActive, IsRegionJP and feature 0x83
    /// (MeratAirTaxi).</summary>
    public bool Vip, VipAirTaxiFeature, FreeAirTaxi, RegionJP, MeratAirTaxiFeature;
    /// <summary>Unk_Inventory::FindItemByTag(10) (air_taxi) and FindItemByTag(0xb) (air_taxi_advanced): the uids, 0 for none.</summary>
    public long AirTaxiUid, AdvancedUid;
    /// <summary>Unk_Inventory::FUN_140a5f7b0(0xb, 0): the usable air_taxi_advanced count.</summary>
    public int AdvancedTickets;
    /// <summary>GetMeratBalance and ClientConstants MeratAirTaxiPrice (+0x8aa8).</summary>
    public long MeratBalance, MeratAirTaxiPrice;
}

/// <summary>
/// CCashCallManager::FUN_140b030b0 (@0x140b030b0, cashcallmanager.cpp:65-149; the tail read from the exe at 0x140b033de-0x140b03575):
/// the air taxi call the world map's TakeCashTaxi starts. It picks the call kind (1 meso, 2 a ticket or free, 3 merat) and the ticket
/// item, then a rider rides off with RideOffActionCashCall (the answer starts the state, mypc.cpp:3420-3425) and anyone else enters
/// StateCashCall (0x2a) with StateCashCallData(kind, item, field).
/// </summary>
public static class CashCall
{
    public const int StateId = 0x2a, StateTaxiCall = 0x25;
    /// <summary>The item string tags air_taxi (10) and air_taxi_advanced (0xb).</summary>
    public const int AirTaxiTag = 10, AdvancedAirTaxiTag = 0xb;
    public const int VipAirTaxiFeature = 0x57, MeratAirTaxiFeature = 0x83;
    /// <summary>StateCashCallData m_nCallType: 1 meso air taxi, 2 cash air taxi (a ticket, VIP or free), 3 merat air taxi.</summary>
    public const int KindMeso = 1, KindTicket = 2, KindMerat = 3;
    /// <summary>ClientConstants MeratAirTaxiPrice's default (constantsblocks.cpp:1127).</summary>
    public const long DefaultMeratAirTaxiPrice = 390;

    public enum Outcome { Refused, Call, RideOff }

    /// <summary>The air_taxi_advanced tickets a field not yet visited takes: 3, 1 in the JP region (cashcallmanager.cpp:106-108,
    /// uiworldmapdialog.cpp:2458-2460).</summary>
    public static int AdvancedTicketsNeeded(bool regionJP) => regionJP ? 1 : 3;

    /// <summary>
    /// FUN_140b030b0: no player, a dead one, or one in StateTaxiCall / StateCashCall: refused. A VIP with feature 0x57: kind 2 with no
    /// item. A visited field: kind 2 with no item while a free air taxi event runs, else the first air_taxi item, or the first
    /// air_taxi_advanced one, with kind 2, else kind 1 (mesos). A field not visited: with enough air_taxi_advanced items the first one
    /// (none found: refused), else with feature 0x83 and enough merats kind 3, else refused. Then a rider rides off; anyone else
    /// enters 0x2a when FUN_141527160(0x2a) lets it (else refused).
    /// </summary>
    public static Outcome Start(in CashCallFacts f, out int kind, out long itemUid)
    {
        kind = KindMeso;
        itemUid = 0;
        if (!f.HasPlayer || f.Dead) return Outcome.Refused;
        if (f.StateId == StateTaxiCall || f.StateId == StateId) return Outcome.Refused;
        if (f.Vip && f.VipAirTaxiFeature)
            kind = KindTicket;
        else if (f.Visited)
        {
            if (f.FreeAirTaxi) kind = KindTicket;
            else
            {
                long uid = f.AirTaxiUid != 0 ? f.AirTaxiUid : f.AdvancedUid;
                if (uid != 0) { itemUid = uid; kind = KindTicket; }
            }
        }
        else if (f.AdvancedTickets >= AdvancedTicketsNeeded(f.RegionJP))
        {
            if (f.AdvancedUid == 0) return Outcome.Refused;
            itemUid = f.AdvancedUid;
            kind = KindTicket;
        }
        else if (f.MeratAirTaxiFeature)
        {
            if (f.MeratBalance < f.MeratAirTaxiPrice) return Outcome.Refused;
            kind = KindMerat;
        }
        else return Outcome.Refused;
        if (f.Riding) return Outcome.RideOff;
        if (!f.CanEnterCashCall) return Outcome.Refused;
        return Outcome.Call;
    }

    /// <summary>Unk_Inventory::FUN_140a5f7b0(tag, 0) (@0x140a5f7b0, inventory.cpp:835): the summed amount of the usable items with
    /// the string tag; 0 for tag 0.</summary>
    public static int CountByTag(IEnumerable<(long Uid, long ExpiryTime, bool Usable, int Tag, int Amount)> items, int tag)
    {
        if (tag == 0 || items == null) return 0;
        int n = 0;
        foreach (var (_, _, usable, t, amount) in items)
            if (t == tag && usable) n += amount;
        return n;
    }
}

/// <summary>What StateCashCall does to the world through its owner, the local player.</summary>
public interface ICashCallHost
{
    /// <summary>FUN_140710590.</summary>
    void Send(IOutPacket packet);
    /// <summary>g_143ab2a90.FindItemByUid then FUN_140a63880(item, argument) (Unk_Inventory::RequestUseItem); false when the bag has
    /// no such item.</summary>
    bool UseItem(long uid, string argument);
    /// <summary>FindItemByUid(uid)'s name (CItem::FUN_140527b10); null when the bag has no such item.</summary>
    string ItemName(long uid);
    /// <summary>ShowSystemNotice(text, 1, 1).</summary>
    void SystemNotice(string text);
    /// <summary>GetStringCommon(id).</summary>
    string Common(int id);
    /// <summary>CommonResource's member by its commonResource.xml key; "" when absent.</summary>
    string CommonResource(string key);
    /// <summary>The casting bar 0x4e72: StartCastingText(0, text, ms, false, true, "") and FUN_1418e1390 (stop).</summary>
    void StartCasting(string text, int durationMs);
    void CancelCasting();
    /// <summary>PlayAnimation(name, 1.0, false, false) and PlayAnimationById(0) (the idle).</summary>
    void PlayAnimation(string name);
    void PlayIdle();
    /// <summary>PCBase::vfunc_23 (pcbase.cpp, GameObject::vfunc_23 @0x14123c5c0): 1 hides the pc (and its ridee), 0 shows it.</summary>
    void SetShown(bool shown);
    /// <summary>SpawnEffect (@0x1410238d0): the effect 150 behind the player, turned to it.</summary>
    void SpawnEffect(string path);
}

/// <summary>
/// StateCashCall (actor state 0x2a, client/ms2/statecashcalldata.cpp) for the local player. vfunc_4 (@0x141022c50, :148-180) plays the
/// call's animation and runs the casting bar (0x64c, the bank's 0x65a) for the call's duration, ending 100 ms after it; vfunc_3
/// (@0x141023110, :236-257) moves the phases on: 0x6b cast -> OnCastEnd (:320) spawns the station effect and waits 3.2 s (1 s for the
/// cash air taxi's), or with no effect goes to 0x6d at once (+100 ms); 0x6c -> OnArrived (:341) hides the pc for the air taxis and
/// waits 1 s; 0x6d -> OnLeave (:358) sends the request (SendRequest :378) and waits 3 s; after that the state returns to idle
/// (vfunc_7(1), :186). vfunc_5 (leave, :195-215) shows the item's "not used" notice 0xa0 when the request was not sent.
/// STILL-OPEN: the held item's SendRequestCube (vfunc_4 :170), the hit reaction (vfunc_6 :291), and the medic's leave delay and
/// notice (kind 5) and the bank (kind 6), which this port does not start.
/// </summary>
public sealed class CashCallState
{
    public const int Cast = 0x6b, Arrive = 0x6c, Leave = 0x6d, Away = 0x6e;
    public const int CastTextId = 0x64c, BankCastTextId = 0x65a, NotUsedNoticeId = 0xa0;
    /// <summary>CommonResource EFFECT_AIRTAXI_CASH (+0x3a0): its wait is 1 s, any other effect's 0xc80 ms (OnCastEnd :329-331).</summary>
    public const string CashEffectKey = "EFFECT_AIRTAXI_CASH";

    readonly ICashCallHost host;
    long elapsed, end;      // m_nElapsed, m_nEnd as offsets from m_nStart (ms)

    public CashCallState(ICashCallHost host) { this.host = host; }

    /// <summary>m_n10, 0 before <see cref="Begin"/>.</summary>
    public int Phase { get; private set; }
    /// <summary>The state returned to idle or was left.</summary>
    public bool Finished { get; private set; } = true;
    /// <summary>m_bSent: the request went out.</summary>
    public bool Sent { get; private set; }
    /// <summary>StateCashCallData m_nCallType, m_nItemUid and m_nPrice (the field the call goes to).</summary>
    public int Kind { get; private set; }
    public long ItemUid { get; private set; }
    public int FieldId { get; private set; }
    public bool Running => !Finished;

    /// <summary>GetEffectName (@0x141023b50): the animation the owner plays.</summary>
    public static string AnimationName(int kind) => kind switch
    {
        4 => "BlackMarketHelicopter_A",
        5 => "EmergencyHelicopter_A",
        6 => "Object_React_A",
        _ => "AirTaxi_A",
    };

    /// <summary>GetStationName (@0x141023d50): the CommonResource key of the vehicle effect; null for none.</summary>
    public static string StationEffectKey(int kind) => kind switch
    {
        2 or 3 => CashEffectKey,
        1 => "EFFECT_AIRTAXI_MESO",
        4 => "EFFECT_CASH_CALL_BLACK_MARKET",
        5 => "EFFECT_CASH_CALL_MEDIC",
        _ => null,
    };

    /// <summary>GetDuration (@0x141023c20): ClientConstants TaxiCallDuration (3000), AirTaxiCashCallDuration (0) for kinds 2 / 3,
    /// AirTaxiMesoCallDuration (0) for 1, BankCallDuration (1000) for 6; 200 less when the owner is not the local player.</summary>
    public static int Duration(int kind, Func<string, int, int> constant, bool local)
    {
        int ms = constant("TaxiCallDuration", 3000);
        switch (kind)
        {
            case 2: case 3: ms = constant("AirTaxiCashCallDuration", 0); break;
            case 1: ms = constant("AirTaxiMesoCallDuration", 0); break;
            case 6: ms = constant("BankCallDuration", 1000); break;
        }
        if (!local) ms -= 200;
        return ms;
    }

    /// <summary>vfunc_4 for the local player with StateCashCallData(kind, item, field), for <paramref name="durationMs"/>
    /// (<see cref="Duration"/>).</summary>
    public void Begin(int kind, long itemUid, int fieldId, int durationMs)
    {
        Kind = kind;
        ItemUid = itemUid;
        FieldId = fieldId;
        Sent = false;
        Finished = false;
        Phase = Cast;
        host.PlayAnimation(AnimationName(kind));
        elapsed = 0;
        end = durationMs + 100;
        host.StartCasting(host.Common(kind == 6 ? BankCastTextId : CastTextId) ?? "", durationMs);
    }

    /// <summary>vfunc_3: <paramref name="seconds"/> of game time (FUN_14028d260, (int)(f * 1000.0f)).</summary>
    public void Tick(double seconds)
    {
        if (Finished) return;
        elapsed += (int)((float)seconds * 1000.0f);
        if (elapsed < end) return;
        switch (Phase)
        {
            case Cast: OnCastEnd(); break;
            case Arrive: OnArrived(); break;
            case Leave: OnLeave(); break;
            default: ToIdle(); break;
        }
    }

    /// <summary>vfunc_8 (@0x141023230, :262-276) with the move direction: in 0x6b a move input sends the owner to the walk state (2),
    /// which leaves this one (<see cref="LeaveState"/>); in the other phases vfunc_8 returns first and the state holds the body (vfunc_3
    /// moves it by zero). True: the move input is held this frame.</summary>
    public bool HoldsMove(bool moveInput)
    {
        if (Finished) return false;
        if (moveInput && Phase == Cast) { LeaveState(); return false; }
        return true;
    }

    /// <summary>vfunc_5 (@0x141022f60): the state is left. With the request not sent, the item's notice 0xa0 (its name) when the bag
    /// still has it; the casting bar stops, the pc shows again (vfunc_23(0)).</summary>
    public void LeaveState()
    {
        if (Finished) return;
        if (!Sent && ItemUid != 0 && host.ItemName(ItemUid) is { } name)
            host.SystemNotice(Ms2.Quest.ClientFormat.Format(host.Common(NotUsedNoticeId) ?? "", name));
        host.CancelCasting();
        host.SetShown(true);
        Sent = false;
        Finished = true;
    }

    /// <summary>OnCastEnd (@0x1410233d0).</summary>
    void OnCastEnd()
    {
        host.CancelCasting();
        string key = StationEffectKey(Kind);
        string effect = key == null ? "" : host.CommonResource(key) ?? "";
        if (effect.Length != 0)
        {
            host.SpawnEffect(effect);
            Next(Arrive, effect == host.CommonResource(CashEffectKey) ? 1000 : 0xc80);
        }
        else Next(Leave, 100);
        host.PlayIdle();
    }

    /// <summary>OnArrived (@0x1410234d0): the air taxis (kinds 1-3) hide the pc and wait 1 s, others 100 ms.</summary>
    void OnArrived()
    {
        int wait = 100;
        if (Kind is 1 or 2 or 3) { host.SetShown(false); wait = 1000; }
        Next(Leave, wait);
    }

    /// <summary>OnLeave (@0x141023560): the request, then 3 s for kinds 1-4 (100 ms otherwise; the medic's delay is STILL-OPEN).</summary>
    void OnLeave()
    {
        SendRequest();
        Next(Away, Kind is 1 or 2 or 3 or 4 ? 3000 : 100);
    }

    /// <summary>SendRequest (@0x141023650): no item: RequestTaxi mode 3 (kinds 1 and 2) or 4 (kind 3) with the field; an item: its use
    /// with the field as the argument ("{0}"), when the bag still has it.</summary>
    void SendRequest()
    {
        if (ItemUid == 0)
        {
            if (Kind is 1 or 2 or 3) host.Send(new RequestTaxiCash(Kind, FieldId));
        }
        else host.UseItem(ItemUid, ((uint)FieldId).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Sent = true;
    }

    /// <summary>vfunc_7(1) (@0x141022ee0): the pc shows again and goes to idle, which leaves the state (vfunc_5 with the request sent).</summary>
    void ToIdle()
    {
        host.SetShown(true);
        LeaveState();
    }

    /// <summary>SetPhase (@0x141023890).</summary>
    void Next(int phase, int addMs)
    {
        Phase = phase;
        end += addMs;
    }
}
