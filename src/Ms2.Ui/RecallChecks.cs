using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;

namespace Ms2.Ui;

/// <summary>What the recall checks read of the own pc, the field and the world (CCashCallManager::CanRecall,
/// cashcallmanager.cpp:294, and UIPartyDialog::FUN_141a77eb0, uipartydialog.cpp:894).</summary>
public struct RecallFacts
{
    /// <summary>CContentShutdown::Get(0x1a): the recall content is shut down (notice 0x2f7).</summary>
    public bool Shutdown;
    /// <summary>WorldService::GetLocalPlayer is set.</summary>
    public bool HasPlayer;
    /// <summary>PCBase::IsDead.</summary>
    public bool Dead;
    /// <summary>GetCurStateId of the own pc.</summary>
    public int StateId;
    /// <summary>PCBase::FUN_141675ed0 (pcbase.cpp:4328): the hit report's entity is water.</summary>
    public bool OnWater;
    /// <summary>DAT_1439f4a7c: the channel of the last ServerEnter (0: none announced).</summary>
    public int Channel;
    /// <summary>FUN_140930b10 (unit_14092f420_accountsessionstate.cpp:779): DAT_143ab2878, the field's service type, is not 0.</summary>
    public bool ServiceType;
    /// <summary>CFieldData::IsRecallOtherUserAllowed (fielddata.cpp:811): the field's &lt;cashCall RecallOtherUserProhibit&gt; is off.</summary>
    public bool FieldAllowsRecall;
    /// <summary>CFieldData::vfunc_2 (fielddata.cpp:867): the field's capacity, m_nCapacity (0: no limit).</summary>
    public int Capacity;
    /// <summary>WorldService::FUN_141bc2bc0 (worldservice_1.cpp:4117): the proxies that are the own pc or another player.</summary>
    public int Players;
    /// <summary>WorldService::FUN_141bc2b30 (worldservice_1.cpp:671): the field portals' +0x188 values summed.</summary>
    public int PortalUsers;
    /// <summary>MyPC::FUN_141527160(0x2c) (mypc.cpp:1872-1882): the own state is 1, 0xb, 0xf, 0x21 or 0x3a.</summary>
    public bool StateAllows;
    /// <summary>PCBase::IsRiding.</summary>
    public bool Riding;
}

/// <summary>
/// The recall scrolls (the guild, party and wedding recall items and the party window's recall button): the checks, then
/// StateRecall (0x2c, <see cref="RecallCast"/>) or, riding, a ride off that starts it after the answer.
/// </summary>
public static class RecallChecks
{
    /// <summary>The common strings the checks show (FUN_140d73bc0(id, 0, 0, "")): 0x2f7 content shut down, 0xb57 dead or
    /// already recalling, 0xb59 the state, 0xb58 the channel or field, 0xb5f the field's capacity, 0xb5a not in a guild,
    /// 0xb5c no other guild member, 0xb5b not in a party, 0xb5d no other party member.</summary>
    public const int ShutdownNotice = 0x2f7, DeadNotice = 0xb57, StateNotice = 0xb59, FieldNotice = 0xb58, CapacityNotice = 0xb5f,
        NotInGuildNotice = 0xb5a, NoGuildMemberNotice = 0xb5c, NotInPartyNotice = 0xb5b, NoPartyMemberNotice = 0xb5d;
    /// <summary>RecallWedding's box (ShowMessageBox of the common string, itemfunctiondispatch.cpp:1380, :1385).</summary>
    public const string NotMarriedKey = "s_err_cash_recall_not_marriage";
    /// <summary>StateRecall's state id (pc.cpp:705).</summary>
    public const int StateRecall = 0x2c;
    /// <summary>The party window's scroll: Unk_Inventory::FindItemByTag(0x14), the item tag "party_call".</summary>
    public const int PartyCallTag = 0x14;
    /// <summary>StateRecallData's target for the merat recall (no scroll): the finished cast sends Party 0x1d.</summary>
    public const long NoScroll = -1;

    /// <summary>What a recall start does.</summary>
    public enum Outcome
    {
        /// <summary>A check refused: <c>notice</c> is shown when not 0.</summary>
        Refused,
        /// <summary>vfunc_59(0x2c, StateRecallData(target)): <see cref="RecallCast.Start"/>.</summary>
        Cast,
        /// <summary>Riding: SendRequestRideMode1(RideOffActionRecall(target)); the ride-off answer starts the cast.</summary>
        RideOff,
    }

    /// <summary>
    /// CCashCallManager::CanRecall (@0x140b040a0, cashcallmanager.cpp:294) for <paramref name="count"/> callees: no own pc,
    /// refused without a notice; dead or already in 0x2c: 0xb57; a state that is not 1, or on water: 0xb59; no channel, a
    /// service type, or a field that prohibits it: 0xb58; with a capacity, players + count + the portals' sum over it: 0xb5f.
    /// Returns true when it passes.
    /// </summary>
    public static bool CanRecall(in RecallFacts f, int count, out int notice)
    {
        notice = 0;
        if (!f.HasPlayer) return false;
        if (f.Dead || f.StateId == StateRecall) { notice = DeadNotice; return false; }
        if (f.StateId != 1 || f.OnWater) { notice = StateNotice; return false; }
        if (f.Channel == 0 || f.ServiceType || !f.FieldAllowsRecall) { notice = FieldNotice; return false; }
        if (f.Capacity > 0 && f.Players + (count + f.PortalUsers) > f.Capacity) { notice = CapacityNotice; return false; }
        return true;
    }

    /// <summary>
    /// CCashCallManager::FUN_140b04430 (@0x140b04430, cashcallmanager.cpp:341): no own pc or <see cref="CanRecall"/> refused:
    /// refused; riding: the ride off with the recall; else FUN_141527160(0x2c) false: 0xb59; else the cast.
    /// </summary>
    public static Outcome Start(in RecallFacts f, int count, out int notice)
    {
        if (!CanRecall(f, count, out notice)) return Outcome.Refused;
        if (f.Riding) return Outcome.RideOff;
        if (!f.StateAllows) { notice = StateNotice; return Outcome.Refused; }
        return Outcome.Cast;
    }

    /// <summary>
    /// RecallGuild (@0x140d226b0, itemfunctiondispatch.cpp:1346): content 0x1a shut down: 0x2f7; not in a guild: 0xb5a; the
    /// guild's recallable members (<see cref="GuildMembersOutsideTutorial"/>) minus one is 0 or less: 0xb5c; else
    /// <see cref="Start"/> for that many.
    /// </summary>
    public static Outcome Guild(in RecallFacts f, bool inGuild, int guildMembers, out int notice)
    {
        notice = 0;
        if (f.Shutdown) { notice = ShutdownNotice; return Outcome.Refused; }
        if (!inGuild) { notice = NotInGuildNotice; return Outcome.Refused; }
        int count = guildMembers - 1;
        if (count <= 0) { notice = NoGuildMemberNotice; return Outcome.Refused; }
        return Start(f, count, out notice);
    }

    /// <summary>
    /// RecallWedding (@0x140d228e0, itemfunctiondispatch.cpp:1371): content 0x1a shut down: 0x2f7; WeddingManager's
    /// IsInProgress or ApplyPartner false: the <see cref="NotMarriedKey"/> box (<c>boxKey</c>); else <see cref="Start"/> for 1.
    /// </summary>
    public static Outcome Wedding(in RecallFacts f, bool married, bool partnerApplies, out int notice, out string boxKey)
    {
        notice = 0;
        boxKey = null;
        if (f.Shutdown) { notice = ShutdownNotice; return Outcome.Refused; }
        if (!married || !partnerApplies) { boxKey = NotMarriedKey; return Outcome.Refused; }
        return Start(f, 1, out notice);
    }

    /// <summary>
    /// UIPartyDialog::FUN_141a77eb0 (@0x141a77eb0, uipartydialog.cpp:894), the recall button's checks: content 0x1a shut
    /// down: 0x2f7; not in a party: 0xb5b; the online members outside a tutorial field minus one is 0 or less: 0xb5d; then
    /// <see cref="CanRecall"/>'s checks for that count, and FUN_141527160(0x2c) false: 0xb59. It has no riding branch: a
    /// riding pc is not in state 1, so the state check refuses it.
    /// </summary>
    public static bool PartyButton(in RecallFacts f, bool inParty, int onlineMembers, out int notice)
    {
        notice = 0;
        if (f.Shutdown) { notice = ShutdownNotice; return false; }
        if (!inParty) { notice = NotInPartyNotice; return false; }
        int count = onlineMembers - 1;
        if (count <= 0) { notice = NoPartyMemberNotice; return false; }
        if (!CanRecall(f, count, out notice)) return false;
        if (!f.StateAllows) { notice = StateNotice; return false; }
        return true;
    }

    /// <summary>
    /// CGuildInfo::FUN_1404d3400 (@0x1404d3400, guilddef_1.cpp:2232): the members that are online (f158 0), full records
    /// (fb0 3) and in a field <paramref name="tutorialField"/> finds that is not a tutorial (HasTutorialType): the field
    /// lookup gives null for an unknown field, which is skipped.
    /// </summary>
    public static int GuildMembersOutsideTutorial(IEnumerable<GuildMember> members, Func<int, bool?> tutorialField)
    {
        int n = 0;
        if (members == null) return 0;
        foreach (var m in members)
        {
            if (m == null || m.Offline != 0 || m.Mode != 3) continue;
            if (tutorialField(m.MapId) is not false) continue;
            n++;
        }
        return n;
    }

    /// <summary>
    /// CPartyManager::CountOnlineMembersOutsideTutorial (@0x14162bf20, partymanager.cpp:840): the members that are not
    /// offline and whose field (user m_28) is found and is not a tutorial.
    /// </summary>
    public static int PartyMembersOutsideTutorial(IEnumerable<PartyMember> members, Func<int, bool?> tutorialField)
    {
        int n = 0;
        if (members == null) return 0;
        foreach (var m in members)
        {
            if (m == null || m.Offline) continue;
            if (tutorialField(m.User.CurrentMapId) is not false) continue;
            n++;
        }
        return n;
    }

    /// <summary>
    /// Unk_Inventory::FindItemByTag (@0x140a5e040, inventory.cpp:630): of the usable items with the tag, the one
    /// FUN_140a5c650 (:380) sorts first: an item with an expiry time before one without, the earlier expiry first. Among items
    /// the sort keeps equal (no expiry, or the same one), the bag order is taken. 0 when none.
    /// </summary>
    public static long FirstByTag(IEnumerable<(long Uid, long ExpiryTime, bool Usable, int Tag)> items, int tag)
    {
        if (tag == 0 || items == null) return 0;
        long best = 0, bestExpiry = 0;
        bool found = false;
        foreach (var (uid, expiry, usable, t) in items)
        {
            if (t != tag || !usable) continue;
            if (!found) { found = true; best = uid; bestExpiry = expiry; continue; }
            // FUN_140a5c650(candidate, best): the candidate has an expiry, and best has none or a later one
            if (expiry > 0 && (bestExpiry <= 0 || expiry < bestExpiry)) { best = uid; bestExpiry = expiry; }
        }
        return found ? best : 0;
    }
}

/// <summary>What <see cref="RecallCast"/> reads from the game and how it answers.</summary>
public interface IRecallHost
{
    bool HasPlayer { get; }
    void Send(IOutPacket packet);
    /// <summary>UICastingBar 0x4e72 StartCastingText(listener, text, ms, false, true, ""). False without a casting bar;
    /// the casting time then runs on <see cref="NowMs"/>.</summary>
    bool StartCasting(ICastingEventListener listener, string text, int durationMs);
    /// <summary>UICastingBar 0x4e72 FUN_1418e1390 (cancel).</summary>
    void CancelCasting();
    long NowMs { get; }
    void PlayPlayerAnim(string animName, bool loop);
    void StopPlayerInteractAnim();
    /// <summary>g_143ab2a90.FindItemByUid(uid) then FUN_140a63880(item, ""): the plain item use request. False when the bag
    /// no longer has the item (nothing is sent).</summary>
    bool UseItem(long uid);
}

/// <summary>
/// StateRecall (0x2c, statesummon.cpp:479-587): "Object_React_A", the casting bar with the common string 0x657 for
/// ClientConstants RecallCastingTime (+0x3628, 5000 when the tables do not set it); a finished cast uses the scroll (its
/// uid) or, with no scroll (-1), sends Party 0x1d, then returns to state 1; move input leaves for state 2, whose exit
/// cancels the bar.
/// STILL-OPEN: vfunc_4's held item put back (SendRequestCube), the owner's zero move (vfunc_22 of the move direction) and
/// FUN_14166e0a0 at the exit, and FUN_1417b9460's queued skill while no key is held.
/// </summary>
public sealed class RecallCast : ICastingEventListener
{
    /// <summary>The casting bar's common string (StateRecall::vfunc_4, statesummon.cpp:515).</summary>
    public const int LabelId = 0x657;
    /// <summary>vfunc_4's sequence (statesummon.cpp:505-506).</summary>
    public const string Animation = "Object_React_A";
    /// <summary>ClientConstants RecallCastingTime's default (unit_1402ab440_constantsblocks.cpp:887).</summary>
    public const int DefaultCastingTime = 5000;

    readonly IRecallHost host;
    bool active, finished;
    long target, endMs;

    public RecallCast(IRecallHost host) { this.host = host; }

    /// <summary>The recall state runs (GetCurStateId 0x2c).</summary>
    public bool IsCasting => active;
    /// <summary>StateRecallData m_p18: the scroll's uid, or -1 for the merat recall; 0 when no cast runs.</summary>
    public long Target => active ? target : 0;

    /// <summary>
    /// vfunc_4 (@0x1417f86a0): m_n48 = 0, the target kept, "Object_React_A", and the casting bar with
    /// <paramref name="label"/> (GetStringCommon(0x657)) for <paramref name="castMs"/>.
    /// </summary>
    public void Start(long scrollOrNoScroll, int castMs, string label)
    {
        if (!host.HasPlayer) return;
        if (active) Cancel();
        target = scrollOrNoScroll;
        active = true;
        finished = false;
        host.PlayPlayerAnim(Animation, false);
        endMs = 0;
        if (!host.StartCasting(this, label ?? "", castMs)) endMs = host.NowMs + castMs;
    }

    /// <summary>OnCastingFinished (@0x1417f8bb0): m_n48 = 1.</summary>
    public void OnCastingFinished() { if (active) finished = true; }

    /// <summary>vfunc_6 (@0x1417f8b60): back to state 1.</summary>
    public void OnCastingCancelled() { if (active) End(); }

    /// <summary>
    /// vfunc_8 (@0x1417f8920): a finished cast sends Party 0x1d for the target -1, else uses the scroll when the bag still
    /// has it, then state 1; else move input leaves for state 2 (the exit, vfunc_5, cancels the bar). Returns true when the
    /// state ended. The own pc gone ends the state without a send or a bar cancel (the bar went with the old field's HUD).
    /// </summary>
    public bool Tick(bool moveInput)
    {
        if (!active) return false;
        if (!host.HasPlayer) { End(); return true; }
        if (!finished && endMs != 0 && host.NowMs >= endMs) finished = true;
        if (finished)
        {
            if (target == RecallChecks.NoScroll) host.Send(new PartyRecallRequest());
            else host.UseItem(target);
            End();
            return true;
        }
        if (moveInput) { Cancel(); return true; }
        return false;
    }

    /// <summary>vfunc_5 (@0x1417f88b0): leaving the state cancels the casting bar.</summary>
    public void Cancel()
    {
        if (!active) return;
        End();
        host.CancelCasting();
    }

    void End()
    {
        active = false;
        finished = false;
        host.StopPlayerInteractAnim();
    }
}
