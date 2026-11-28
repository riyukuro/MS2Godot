using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Ms2.Net.Packets;

namespace Ms2Godot.Net;

/// <summary>
/// The live run's field PvP steps (MS2_LIVE_COMBAT_STEP): raw PlayerKillNotice and Pvp 'd' packets the server's debug
/// packet command sends, read the way OnPlayerKillNotice and OnPvp read them, and the pk zone flag after a field change.
/// </summary>
public partial class LiveSession
{
    /// <summary>A raw PlayerKillNotice mode 0 (OnPlayerKillNotice's layout): the killer and the victim.</summary>
    internal string KillNoticeBytes(string killer, string victim)
    {
        var b = new List<byte>();
        Op(b, game.Profile.Number(Ms2.Net.Inbound.PlayerKillNotice)); b.Add(0);
        Unicode(b, killer); Unicode(b, victim);
        return Hex(b);
    }

    /// <summary>A raw PlayerKillNotice mode 1: a StringIdInterface text of the id with one argument.</summary>
    internal string KillStreakBytes(int stringId, string arg)
    {
        var b = new List<byte>();
        Op(b, game.Profile.Number(Ms2.Net.Inbound.PlayerKillNotice)); b.Add(1);
        b.Add(1); I32(b, 1); I32(b, stringId); I32(b, 1); Unicode(b, arg);
        return Hex(b);
    }

    /// <summary>A raw Pvp 'd' (OnPvp's layout): the object id, the point, the team and the mode.</summary>
    internal string PvpPointBytes(int objectId, int point, byte team, byte mode)
    {
        var b = new List<byte>();
        Op(b, game.Profile.Number(Ms2.Net.Inbound.Pvp)); b.Add((byte)'d');
        I32(b, objectId); I32(b, point); b.Add(team); b.Add(mode);
        return Hex(b);
    }

    internal async Task<bool> PvpStep(string step, Ui.HudScene hud, Func<int, Task> frames, Func<Func<bool>, int, Task> until,
        Action<string> shot, Action<string, bool> check)
    {
        var chat = Ui.HudScene.ChatLog;
        string KillRow(int i)
        {
            var h = hud.KillLog?.Host;
            if (h?.Entity == null) return "no movie";
            var m = h.Child("m_member" + i);
            if (m == null) return "no member";
            string F(string n) => Ms2.Avm2.Rt.ToString(h.Get(h.Get(m, n), "text"));
            return $"visible {Ms2.Avm2.Rt.ToBoolean(h.Get(m, "visible"))}, attacker \"{F("m_attackerName")}\", target \"{F("m_targetName")}\"";
        }
        if (step.StartsWith("pvpkill:"))      // pvpkill:<me|foe>[:<other name>]: a raw kill notice, the own pc the killer (me) or the victim (foe)
        {
            var a = step[8..].Split(':');
            string me = Me.User?.Name ?? "", other = a.Length > 1 ? a[1] : "Rival";
            bool own = a[0] == "me";
            int got = 0, logsBefore = chat.Logs.Count;
            Action<PlayerKillNotice> on = n => { if (n.Mode == 0) got++; };
            PlayerKillNoticeReceived += on;
            bool sent = hud.SubmitChatLine("/debug packet " + (own ? KillNoticeBytes(me, other) : KillNoticeBytes(other, me)));
            await until(() => got > 0, 60 * 5);
            await until(() => hud.KillLog?.Host?.Entity != null && KillRow(1).Contains(other), 60 * 5);
            await frames(30);
            PlayerKillNoticeReceived -= on;
            var lines = chat.Logs.Skip(logsBefore).Where(l => l.Kind == Ms2.Ui.Hud.PvPKillLogController.ChatKind).Select(l => l.Msg).ToList();
            string row = KillRow(1);
            GD.Print($"live: pvp {step}: sent {sent}, read {got}, kill log open {hud.KillLog is { IsClosed: false }}, row 1 [{row}], " +
                $"row 2 [{KillRow(2)}], chat 0x14 lines {lines.Count}: {string.Join(" | ", lines.Select(l => l.Length > 160 ? l[..160] : l))}");
            shot($"pvpkill_{a[0]}.png");
            check($"the kill notice ({(own ? "own kill" : "own death")}) shows the row and logs the other name",
                got == 1 && hud.KillLog is { IsClosed: false } && row.Contains($"attacker \"{(own ? me : other)}\"") &&
                row.Contains($"target \"{(own ? other : me)}\"") && lines.Count == 1 && lines[0].Contains(other));
            return true;
        }
        if (step.StartsWith("pvpstreak:"))      // pvpstreak:<client string id>[:<arg>]: a raw kill notice mode 1, the system notice of the id's text
        {
            var a = step[10..].Split(':');
            int id = Convert.ToInt32(a[0], a[0].StartsWith("0x") ? 16 : 10);
            string arg = a.Length > 1 ? a[1] : "Rival";
            var shown = new List<(string, int, bool)>();
            var old = chat.ShowSystemNotice;
            chat.ShowSystemNotice = (t, type, log) => { shown.Add((t, type, log)); old?.Invoke(t, type, log); };
            int got = 0;
            Action<PlayerKillNotice> on = n => { if (n.Mode == 1 && n.Text?.Id == id) got++; };
            PlayerKillNoticeReceived += on;
            bool sent = hud.SubmitChatLine("/debug packet " + KillStreakBytes(id, arg));
            await until(() => got > 0, 60 * 5);
            await frames(20);
            PlayerKillNoticeReceived -= on;
            chat.ShowSystemNotice = old;
            GD.Print($"live: pvp {step}: sent {sent}, read {got}, notices [{string.Join(" | ", shown.Select(s => $"type {s.Item2} log {s.Item3} \"{s.Item1}\""))}]");
            shot($"pvpstreak_{id:x}.png");
            check($"the streak notice {id:x} shows its text as a type 4 notice with the log",
                got == 1 && shown.Count == 1 && shown[0].Item2 == 4 && shown[0].Item3 && shown[0].Item1.Length > 0 && !shown[0].Item1.StartsWith("s_"));
            return true;
        }
        if (step.StartsWith("pvppoint:"))      // pvppoint:<point>:<team>:<mode>: a raw Pvp 'd' for the own pc, then the own CPvPInfo
        {
            var a = step[9..].Split(':').Select(int.Parse).ToArray();
            bool sent = hud.SubmitChatLine("/debug packet " + PvpPointBytes(selfObject, a[0], (byte)a[1], (byte)a[2]));
            await until(() => OwnPvp.Point == a[0], 60 * 5);
            var p = OwnPvp;
            GD.Print($"live: pvp {step}: sent {sent}, own point {p.Point} team {p.Team} mode {p.Mode} has point {p.HasPoint} pc team {p.PcTeam}");
            check($"Pvp 'd' sets the own point {a[0]}", p.Point == a[0] && p.Team == a[1] && p.Mode == a[2]);
            return true;
        }
        if (step.StartsWith("pctag:"))      // pctag:<name>[:hostile|plain]: another player of the field (waits 60 s for it), the own relation and its tag colour
        {
            var a = step[6..].Split(':');
            await until(() => remote?.PcByName(a[0]) != null, 60 * 60);
            await frames(30);
            var pc = remote?.PcByName(a[0]);
            var c = remote?.HostileTagColor;
            GD.Print($"live: pvp {step}: field {FieldId}, found {pc != null}, object {pc?.ObjectId}, pvp {(pc is { } q ? remote.PvpOf(q.ObjectId) : null)}, " +
                $"own {OwnPvp}, hostile {pc?.Hostile}, tag colour {pc?.TagColor}, hostile colour {c}, bar [{(pc is { } pb ? remote.PcBarLine(pb.ObjectId) : "")}]");
            shot($"pctag_{a[0]}.png");
            if (a.Length > 1)
                check($"{a[0]}'s tag is {a[1]}", pc is { } p && p.Hostile == (a[1] == "hostile") && (p.TagColor == c) == (a[1] == "hostile"));
            return true;
        }
        if (step.StartsWith("pchit:"))      // pchit:<name>:N[:<skill id>]: a run to another player, faced, the attack key (or the skill) N times a second apart; a kill notice ends it
        {
            var a = step[6..].Split(':');
            uint skill = a.Length > 2 ? uint.Parse(a[2]) : 0;
            int killed = 0;
            Action<PlayerKillNotice> on = n => { if (n.Mode == 0 && n.Victim == a[0]) killed++; };
            PlayerKillNoticeReceived += on;
            for (int i = int.Parse(a[1]); i > 0 && killed == 0 && remote?.PcByName(a[0]) is { } pc; i--)
            {
                float dist = hud.DriveTowardScripted(pc.Root.GlobalPosition, false);
                for (int n = 0; n < 60 * 10 && dist > 80 && GodotObject.IsInstanceValid(pc.Root); n++) { dist = hud.DriveTowardScripted(pc.Root.GlobalPosition, false); await frames(1); }
                hud.DriveEnd();
                dist = hud.FaceScripted(pc.Root.GlobalPosition);
                bool cast = skill != 0 ? hud.CastSkillScripted(skill) : hud.AttackScripted();
                if (i % 5 == 0) GD.Print($"live: pvp {step}: distance {dist:F0}: {(cast ? "cast" : "refused")}, hp {pc.Hp}, bar [{remote.PcBarLine(pc.ObjectId)}]");
                await frames(60);
            }
            await frames(60);
            PlayerKillNoticeReceived -= on;
            GD.Print($"live: pvp {step}: kill notices {killed}, kill log row 1 [{KillRow(1)}], {a[0]}'s bar [{(remote?.PcByName(a[0]) is { } v ? remote.PcBarLine(v.ObjectId) : "gone")}]");
            shot($"pchit_{a[0]}.png");
            check($"the hits on {a[0]} end in a kill notice", killed > 0);
            return true;
        }
        if (step.StartsWith("pvpdied:"))      // pvpdied:<killer>:<seconds>: waits for the server's kill notice of the own death, then the log, the chat line and the death
        {
            var a = step[8..].Split(':');
            string me = Me.User?.Name ?? "";
            int got = 0, logsBefore = chat.Logs.Count;
            Action<PlayerKillNotice> on = n => { if (n.Mode == 0 && n.Victim == me) got++; };
            PlayerKillNoticeReceived += on;
            await until(() => got > 0, 60 * int.Parse(a[1]));
            await frames(90);
            PlayerKillNoticeReceived -= on;
            var lines = chat.Logs.Skip(logsBefore).Where(l => l.Kind == Ms2.Ui.Hud.PvPKillLogController.ChatKind).Select(l => l.Msg).ToList();
            string row = KillRow(1);
            GD.Print($"live: pvp {step}: kill notices {got}, own dead {Me.Dead}, body {(player != null && IsInstanceValid(player) ? player.State : null)}, " +
                $"row 1 [{row}], chat 0x14 lines {lines.Count}: {string.Join(" | ", lines.Select(l => l.Length > 160 ? l[..160] : l))}");
            shot("pvpdied.png");
            // Other 0x14 system lines (a trophy the server grants for the death) share the kind: count the killer's line.
            check($"the server's kill notice of the own death shows {a[0]}'s row, logs it and the own PC is dead",
                got == 1 && Me.Dead && row.Contains($"attacker \"{a[0]}\"") && row.Contains($"target \"{me}\"") && lines.Count(l => l.Contains(a[0])) == 1);
            return true;
        }
        bool HandleLocal(Ms2.Net.Wire.RawPacket p) { Handle(p); return true; }
        if (step.StartsWith("pvpend:"))      // pvpend:<win|lose|clear|rank|notice>: raw Pvp 'e' / 'f' / 0x67 (own row) / 0x69 (string 2421), then the state each sets
        {
            string arg = step[7..];
            var b = new List<byte>();
            Op(b, game.Profile.Number(Ms2.Net.Inbound.Pvp));
            long cid = CharacterId;
            int entered = PvpWinLosesEntered, rows = PvpRanking.Count;
            switch (arg)
            {
                case "win": case "lose": b.Add((byte)'e'); b.Add((byte)(arg == "win" ? 1 : 0)); break;
                case "clear": b.Add((byte)'f'); break;
                case "rank": b.Add(0x67); I64(b, cid); I32(b, 3); I32(b, 2); I32(b, 1); I32(b, 0); break;
                case "notice": b.Add(0x69); I32(b, 2421); Unicode(b, Me.User?.Name ?? ""); break;
            }
            // The server's debug packet command refuses a packet under 4 bytes and 'f' is 3 (no body), so 'f' goes
            // through the own receive path (Handle: the same reader and OnPvp a server packet takes).
            bool sent = arg == "clear" ? HandleLocal(new Ms2.Net.Wire.RawPacket(game.Profile.Number(Ms2.Net.Inbound.Pvp), [(byte)'f']))
                : hud.SubmitChatLine("/debug packet " + Hex(b));
            await frames(60);
            GD.Print($"live: pvp {step}: sent {sent}, team {Me.PvpTeam}, poses {PvpWinLosesEntered - entered}, played {OwnStateEnterPlayed ?? "nothing"}, " +
                $"state {Me.State:x}, ranking rows {rows} -> {PvpRanking.Count}, own rank {PvpRanking.RankOf(cid)}, notice \"{hud.LastPvpKillNotice}\"");
            shot($"pvpend_{arg}.png");
            switch (arg)
            {
                case "win": case "lose": check($"Pvp 'e' {arg} plays the pose", PvpWinLosesEntered == entered + 1 && OwnStateEnterPlayed != null); break;
                case "clear": check("Pvp 'f' empties the ranking", rows > 0 && PvpRanking.Count == 0); break;
                case "rank": check("Pvp 0x67 ranks the own row", PvpRanking.RankOf(cid) == 1); break;
                case "notice": check("Pvp 0x69 shows the streak text with the name", hud.LastPvpKillNotice is { Length: > 0 } t && t.Contains(Me.User?.Name ?? "?") && !t.StartsWith("s_")); break;
            }
            return true;
        }
        if (step.StartsWith("pkzone:"))      // pkzone:<in|out|?>: the pk zone flag the field's fade-in left (UISceneGamePlay's m_c8); ? only prints it
        {
            string arg = step[7..];
            bool want = arg == "in";
            await frames(30);
            string last = chat.Logs.LastOrDefault(l => l.Kind == Ms2.Ui.Hud.PkZoneNotice.ChatKind).Msg ?? "";
            GD.Print($"live: pvp {step}: field {FieldId}, in pk zone {hud.InPkZone}, last 0x14 line: {(last.Length > 200 ? last[..200] : last)}");
            shot($"pkzone_{(want ? "in" : "out")}.png");
            if (arg is "in" or "out") check($"the pk zone flag is {(want ? "set" : "clear")} after the fade-in", hud.InPkZone == want);
            return true;
        }
        return false;
    }
}
