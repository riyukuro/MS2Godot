using Godot;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Net;

// The recall scrolls: RecallGuild, the party window's recall button and StateRecall (0x2c).
public partial class LiveSession
{
    RecallCast recallCast;
    /// <summary>StateRecall (0x2c) of the own pc.</summary>
    public RecallCast RecallCast => recallCast ??= new RecallCast(new RecallHost(this));

    int? fieldFactsId;
    Ms2.Formats.World.FieldData fieldFacts;
    /// <summary>The current field's data (CFieldData of WorldService's field), null when it is not known.</summary>
    Ms2.Formats.World.FieldData CurrentFieldData()
    {
        if (FieldId is not int id) return null;
        if (fieldFactsId == id) return fieldFacts;
        fieldFactsId = id;
        try { fieldFacts = Ms2.Formats.World.FieldData.FindById(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), id); }
        catch { fieldFacts = null; }
        return fieldFacts;
    }

    /// <summary>CFieldData::FindField(id) then HasTutorialType: null when the field is not found.</summary>
    static bool? TutorialField(int id)
    {
        try { return Ms2.Formats.World.FieldData.FindById(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), id)?.HasTutorialType; }
        catch { return null; }
    }

    /// <summary>
    /// The recall checks' facts as far as the port knows them.
    /// STILL-OPEN: CContentShutdown (the server's content shutdown flags) is not kept, so 0x1a reads as on; the state id is
    /// the own pc's current one (<see cref="OwnCurStateId"/>), 0x2c while <see cref="RecallCast"/> runs.
    /// </summary>
    RecallFacts RecallFactsNow()
    {
        bool has = player != null && IsInstanceValid(player) && selfObject > 0;
        var field = CurrentFieldData();
        int state = recallCast is { IsCasting: true } ? RecallChecks.StateRecall : OwnCurStateId(OwnSkillStateRunsOf?.Invoke() ?? false);
        int portalUsers = 0;
        if (Portals != null) foreach (var p in Portals.All) portalUsers += p.Extra;
        return new RecallFacts
        {
            HasPlayer = has,
            Dead = Me.Dead,
            StateId = state,
            OnWater = has && player.OnWater,
            Channel = serverChannel,
            ServiceType = (entry?.Field?.FieldType ?? 0) != 0,
            FieldAllowsRecall = field != null && !field.RecallOtherUserProhibit,
            Capacity = field?.Capacity ?? 0,
            Players = (has ? 1 : 0) + (remote?.UserCount ?? 0),
            PortalUsers = portalUsers,
            StateAllows = LiveInteractObjects.StateAllowsInteraction(state),
            Riding = has && player.Mount != null,
        };
    }

    /// <summary>RecallGuild (@0x140d226b0) for the scroll <paramref name="uid"/>: <see cref="RecallChecks.Guild"/>, then the cast or
    /// the ride off. Returns the item function's result (1 when the cast or the ride off started).</summary>
    public bool RecallGuild(long uid)
    {
        var f = RecallFactsNow();
        int members = RecallChecks.GuildMembersOutsideTutorial(Guilds.Members, TutorialField);
        return StartRecall(RecallChecks.Guild(f, Guilds.InGuild, members, out int notice), uid, notice);
    }

    /// <summary>
    /// onPartyRecall (uipartydialog.cpp:664-688): <see cref="RecallChecks.PartyButton"/>, then the first usable "party_call" item
    /// (FindItemByTag(0x14)) starts StateRecall with its uid (StartRecallState, :964).
    /// STILL-OPEN: without such an item the client checks the merat balance against ClientConstants PartyRecallMerat
    /// (IClientPlatform vfunc_40, the charge window FUN_141add1f0 when short), then asks with box 0xc64 tagged buy_scroll,
    /// whose yes starts StateRecall(-1); the platform's merat check is not ported, so nothing happens.
    /// </summary>
    public void RecallFromPartyWindow()
    {
        var f = RecallFactsNow();
        int online = RecallChecks.PartyMembersOutsideTutorial(Party.Members, TutorialField);
        if (!RecallChecks.PartyButton(f, Party.IsInParty, online, out int notice))
        {
            if (notice != 0) CommonBoxSink?.Invoke(notice);
            return;
        }
        long now = ServerTime;
        long uid = RecallChecks.FirstByTag(PartyCallItems(now), RecallChecks.PartyCallTag);
        if (uid == 0) { NotApplied.Print("live: party recall: no party_call scroll; the merat recall is not ported"); return; }
        StartRecallCast(uid);
    }

    /// <summary>The bag's items for FindItemByTag: uid, expiry, PC::CanUseItem as far as the port decides it
    /// (<see cref="Ms2.Ui.Items.InventoryCount.CanUse"/>), and the data's string tag.</summary>
    IEnumerable<(long, long, bool, int)> PartyCallItems(long now)
    {
        if (ItemDataOf(0) == null && itemData == null) yield break;
        foreach (var e in Items.All)
        {
            var data = itemData.Get(e.Id);
            if (data == null) continue;
            yield return (e.Uid, e.ExpiryTime, Ms2.Ui.Items.InventoryCount.CanUse(e, itemData, CharacterId, now),
                Ms2.Ui.Items.ItemTags.ByName(data.StringTag));
        }
    }

    bool StartRecall(RecallChecks.Outcome outcome, long target, int notice)
    {
        switch (outcome)
        {
            case RecallChecks.Outcome.Cast:
                StartRecallCast(target);
                return true;
            case RecallChecks.Outcome.RideOff:
                // SendRequestRideMode1(RideOffActionRecall(0) with m_n28 = the target); its answer starts the cast
                var ride = player?.Mount?.Data;
                if (RideOff.Request(true, ride != null, ride?.Type ?? 0, ride?.EnableRideOffUI ?? false, 0,
                        false, new RideOffRecallRequest(target), Send) == RideOff.Outcome.CantRide)
                    SystemNoticeSink?.Invoke(MessageBoxData.Common(RideOff.CantRideNoticeId));
                return true;
            default:
                if (notice != 0) CommonBoxSink?.Invoke(notice);
                return false;
        }
    }

    /// <summary>vfunc_59(0x2c, StateRecallData(target)) after SetRotationAndTargetYaw of the own facing: the cast for
    /// ClientConstants RecallCastingTime.</summary>
    void StartRecallCast(long target) =>
        RecallCast.Start(target, Constants()?.Int("RecallCastingTime", RecallCast.DefaultCastingTime) ?? RecallCast.DefaultCastingTime,
            MessageBoxData.Common(RecallCast.LabelId));

    /// <summary>MyPC::vfunc_121's case 8 (mypc.cpp:3467-3472): a ride off answered with a RideOffActionRecall starts
    /// StateRecall with its target.</summary>
    void RecallAfterRideOff(RideOffAction action)
    {
        if (action is RideOffRecall r) StartRecallCast(r.Value28);
    }

    /// <summary>A server state change leaves StateRecall (its vfunc_5 cancels the bar, nothing is sent); StateRecall keeps
    /// State::vfunc_25 (true), so a server 0x2c keeps it (statemanager.cpp:297).</summary>
    void LeaveRecallForServerState(int state)
    {
        if (state != RecallChecks.StateRecall) recallCast?.Cancel();
    }

    sealed class RecallHost : IRecallHost
    {
        readonly LiveSession s;
        public RecallHost(LiveSession s) { this.s = s; }

        public bool HasPlayer => s.player != null && IsInstanceValid(s.player) && s.selfObject > 0;
        public void Send(IOutPacket packet) => s.Send(packet);

        public bool StartCasting(ICastingEventListener listener, string text, int durationMs)
        {
            if (s.CastingBar == null) return false;
            s.CastingBar.StartCastingText(listener, text ?? "", durationMs, showTime: false, activeBar: true, endText: "");
            return true;
        }

        public void CancelCasting() => s.CastingBar?.Cancel();
        public long NowMs => (long)Time.GetTicksMsec();

        public void PlayPlayerAnim(string animName, bool loop)
        {
            if (HasPlayer) s.player.StartInteractAnim(animName, loop);
        }

        public void StopPlayerInteractAnim()
        {
            if (s.player != null && IsInstanceValid(s.player)) s.player.StopInteractAnim();
        }

        /// <summary>FindItemByUid then FUN_140a63880(item, ""): Unk_Inventory::RequestUseItem (inventory.cpp:1201) with no
        /// argument; a dead pc or an expired item without a maid id sends nothing (its boxes come from the inventory window's
        /// path, <see cref="InventoryController.RequestUseItem"/>).</summary>
        public bool UseItem(long uid)
        {
            var item = s.Items.Get(uid);
            if (item == null || s.Me.Dead) return false;
            var data = ItemDataOf(item.Id);
            if (item.ExpiryTime > 0 && item.ExpiryTime <= s.ServerTime && data?.HasMaidId != true) return false;
            s.Send(new Ms2.Net.ItemUseRequest(uid, ""));
            return true;
        }
    }
}
