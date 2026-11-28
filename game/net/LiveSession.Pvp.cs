using Godot;
using Ms2.Net.Packets;

namespace Ms2Godot.Net;

/// <summary>
/// The Pvp packet's field part (CSceneStateGame::OnPvp @0x141732350, scenestategame.cpp:4986) and PlayerKillNotice
/// (OnPlayerKillNotice @0x141733850, :4344, shown by the HUD).
/// <para>Applied: 'd' (the PC's point, team and mode), 'e' (the own win / lose pose), 'f' (CPVPManager::Clear) and the
/// field ranking 0x67-0x6a (<see cref="PvpRanking"/>, the 0x69 kill notice through the HUD).</para>
/// <para>STILL-OPEN: the ranking and result dialogs (0x4e9a, 0x4e9b) the ranking modes open and refresh; the team tables
/// (Unk_PvpEntryBlock, 0x6b-0x71) 'f' also clears; the arena, rank duel, team and guild modes (unread).</para>
/// </summary>
public partial class LiveSession
{
    /// <summary>A PlayerKillNotice (the kill log dialog, its chat lines and the streak notice).</summary>
    public event System.Action<PlayerKillNotice> PlayerKillNoticeReceived;

    /// <summary>The own PC's CPvPInfo (FieldAddUser's, then Pvp 'd').</summary>
    public Ms2.Ui.Skills.PvpInfo OwnPvp => new(Me.PvpPoint, Me.PvpTeam, Me.PvpMode);

    /// <summary>The local player as a hit candidate (the HUD's: id, party, PvP point and team, buff bits), for the other
    /// players' relation to it (<see cref="RemoteLayer"/>'s name tag colour); null without a HUD.</summary>
    public System.Func<Ms2.Ui.Skills.HitCandidate> OwnPcRelation;

    /// <summary>Pvp 0x69 (Unk_PvpTimer::OnMessage, unit_141017950_pvprankinglist.cpp:179-186): the string id and the name
    /// for FormatStringId; the HUD shows it as a system notice of type 4 and plays System_KillSlayer.</summary>
    public event System.Action<PvpKillNotice> PvpKillNoticeReceived;

    Ms2.Ui.Skills.PvpRankingList pvpRanking;
    /// <summary>CPVPManager's field ranking (Unk_PvpTimer): Pvp 0x67, 0x68 and 0x6a fill it, 'f' empties it.</summary>
    public Ms2.Ui.Skills.PvpRankingList PvpRanking => pvpRanking ??= new Ms2.Ui.Skills.PvpRankingList
    {
        FieldPcName = id => Me.User != null && id == Me.User.CharacterId && selfObject >= 0 ? Me.User.Name : remote?.NameOfCharacter(id),
        OwnMode = () => Me.User != null && selfObject >= 0 ? Me.PvpMode : null,
    };

    /// <summary>The live run's count of the Pvp 'e' poses entered, and the last result asked (0x68's rank).</summary>
    internal int PvpWinLosesEntered;
    internal int PvpLastRank;

    void OnPvp(PvpMessage m)
    {
        switch (m)
        {
            case PvpPointInfo d:
                // 'd': WorldService::FindPCById (the own PC or another player of the field), nothing when it is none (:4995-4997)
                if (d.ObjectId == selfObject) { Me.PvpPoint = d.Point; Me.PvpTeam = d.Team; Me.PvpMode = d.PvpMode; }
                else remote?.SetPvpInfo(d.ObjectId, new Ms2.Ui.Skills.PvpInfo(d.Point, d.Team, d.PvpMode));
                if (LiveCheck) GD.Print($"live: pvp point {d.ObjectId} {d.Point} team {d.Team} mode {d.PvpMode}");
                break;
            case PvpWinLose e:
                EnterOwnPvpWinLose(e.Win);
                break;
            case PvpClear:
                // 'f': CPVPManager::Clear (@0x141280c60): the ranking (Unk_PvpTimer::Clear) and the team tables
                // (Unk_PvpEntryBlock, not ported); no PC's CPvPInfo, relation or name tag colour is touched (those follow 'd')
                PvpRanking.Clear();
                if (LiveCheck) GD.Print("live: pvp clear");
                break;
            case PvpRankRow row:
            {
                var (from, to) = PvpRanking.SetScore(row.CharacterId, row.Score, row.Kill, row.Death, row.N1c);
                if (LiveCheck) GD.Print($"live: pvp rank row {row.CharacterId} score {row.Score} kill {row.Kill} death {row.Death} rank {from} -> {to}");
                break;
            }
            case PvpLastRank last:
                PvpRanking.RefreshFirst();
                PvpLastRank = last.Rank;
                if (LiveCheck) GD.Print($"live: pvp last rank {last.Rank}");
                break;
            case PvpKillNotice k:
                if (LiveCheck) GD.Print($"live: pvp kill notice {k.StringId} {k.Name}");
                PvpKillNoticeReceived?.Invoke(k);
                break;
            case PvpDamage dmg:
            {
                var (from, to) = PvpRanking.SetDamage(dmg.CharacterId, dmg.Damage);
                if (LiveCheck) GD.Print($"live: pvp damage {dmg.CharacterId} {dmg.Damage} rank {from} -> {to}");
                break;
            }
        }
    }

    /// <summary>
    /// Pvp 'e' (scenestategame.cpp:5006-5013): with a local PC, MyPC::vfunc_59(0x28, 0, win ? 0x73 : 0x74, 0) (mypc.cpp:1238).
    /// PC::vfunc_124 refuses it while dead, ChangeState keeps a running StatePvPWinLose (only its sub changes, nothing
    /// replays); else StatePvPWinLose's enter plays the team's MODEPVP_WIN / MODEPVP_DEAD entry and its end enters StateIdle
    /// (<see cref="Ms2.Ui.OwnStateExit.EnterPvpWinLose"/>). MyPC's NotifyStatusChanged(9, 0) after it reaches only the base
    /// UIStatusEventListener::vfunc_oa0_12, which is empty (unit_140a93d00_chromasdkclasses.cpp:2532).
    /// STILL-OPEN: the ride state with a mount (PCBase::vfunc_59 hands the change to the mount) and the not-loaded queue.
    /// </summary>
    void EnterOwnPvpWinLose(bool win)
    {
        if (Me.User == null || selfObject < 0) return;
        if (Me.State == 0x28) { if (LiveCheck) GD.Print("live: pvp win/lose kept the running pose"); return; }
        ownStunEntry = true;
        bool entered;
        try { entered = Me.EnterOwnState(0x28); }
        finally { ownStunEntry = false; }
        if (!entered) { if (LiveCheck) GD.Print("live: pvp win/lose refused (dead)"); return; }
        int sub = win ? Ms2.Ui.OwnStateExit.PvpWinSub : Ms2.Ui.OwnStateExit.PvpLoseSub;
        ownStateExit.EnterPvpWinLose(sub, Me.PvpTeam);
        OwnStateEnterPlayed = ownStateExit.Played;
        PvpWinLosesEntered++;
        if (player != null && IsInstanceValid(player)) player.EnterServerState(Ms2.Ui.BodyEntry.Idle);
        if (LiveCheck) GD.Print($"live: pvp {(win ? "win" : "lose")} pose, team {Me.PvpTeam}, plays {ownStateExit.Played ?? "nothing"}, {(ownStateExit.Left < 0 ? "no end" : $"{ownStateExit.Left:0.###} s")}");
    }
}
