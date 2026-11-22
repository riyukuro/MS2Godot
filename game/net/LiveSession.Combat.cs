using System;
using System.Threading.Tasks;
using Godot;
using Ms2Godot.Flow;

namespace Ms2Godot.Net;

public partial class LiveSession
{
    /// <summary>
    /// MS2_LIVE_COMBAT_STEP with MS2_LIVE_COMBAT_SHOTS=<dir>: one scripted combat run against the live server, with shots and
    /// "live: combat ..." result lines (PASS / FAIL per check). Steps, comma separated, run in the one field entry:
    /// warp:<map>: the GM warp, then the field entry (the tutorial map renders black).
    /// attack: a mob spawned by the GM chat command (MS2_LIVE_COMBAT_MOB, an npc id) beside the player, the attack key pressed
    /// once a second until the mob dies (at most 30 presses); checks: the cast was sent, damage numbers came back, the mob's
    /// exp line (its death) came back.
    /// death: mobs spawned (MS2_LIVE_COMBAT_MOB, MS2_LIVE_COMBAT_MOBS of them) until the own character dies (at most 90 s);
    /// checks: the dead flag, the tombstone, the revival dialog (or the auto-revive countdown).
    /// revive: after death, the revival dialog's free button (onRevival 1); checks: the dead flag clears.
    /// loot: an item (MS2_LIVE_COMBAT_ITEM) and meso (MS2_LIVE_COMBAT_MESO_ITEM, MS2_LIVE_COMBAT_MESO) dropped at the player by
    /// the GM item command, the pickup key held; checks: the drop appears, the item gain and the meso gain come back, the bag
    /// and the meso count change.
    /// </summary>
    static long BagSum(LiveSession live) { long n = 0; foreach (var i in live.Items.All) n += i.Amount; return n; }

    public static async Task CombatRun(GameFlow flow, Func<int, Task> frames, Func<Func<bool>, int, Task> until)
    {
        string steps = System.Environment.GetEnvironmentVariable("MS2_LIVE_COMBAT_STEP");
        string shots = System.Environment.GetEnvironmentVariable("MS2_LIVE_COMBAT_SHOTS");
        if (string.IsNullOrEmpty(steps) || flow.GetTree().CurrentScene is not Ui.HudScene hud || Instance is not { } live) return;
        if (!string.IsNullOrEmpty(shots)) DirAccess.MakeDirRecursiveAbsolute(shots);
        void Shot(string name)
        {
            if (string.IsNullOrEmpty(shots)) return;
            flow.GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(shots, name)); GD.Print("live: shot " + name);
        }
        void Check(string what, bool ok) => GD.Print($"live: combat {(ok ? "PASS" : "FAIL")}: {what}");
        string Env(string k, string d) => System.Environment.GetEnvironmentVariable(k) is { Length: > 0 } v ? v : d;
        int numbers = 0, kills = 0, npcs = 0;
        long mesoDelta = 0;
        live.DamageNumbered += (n, npc, pc) => { if (npc != null) numbers++; };
        live.NpcExpShown += (npc, exp) => kills++;
        live.NpcAdded += npc => npcs++;
        live.MesoChanged += (delta, kind) => mesoDelta += delta;
        await frames(240);
        foreach (var step in steps.Split(','))
        {
            GD.Print($"live: combat step {step}");
            if (step.StartsWith("warp:"))
            {
                int entered = 0; void Entered(int map, long ms) => entered++;
                live.FieldEntered += Entered;
                GD.Print($"live: combat {step}: {(hud.SubmitChatLine("/warp " + step[5..]) ? "sent" : "no chat window")}");
                await until(() => entered > 0, 60 * 90); await frames(240);
                live.FieldEntered -= Entered;
                Check($"the field entry after the warp (map {live.CurrentEntry?.MapId})", entered > 0);
                continue;
            }
            if (step.StartsWith("level:"))
            {
                int level = int.Parse(step[6..]);
                GD.Print($"live: combat {step}: {(hud.SubmitChatLine("/player level " + level) ? "sent" : "no chat window")}");
                await until(() => live.Me.User.Level == level, 60 * 20); await frames(60);
                Check($"the character is level {level}", live.Me.User.Level == level);
                continue;
            }
            switch (step)
            {
                case "attack":
                {
                    int mob = int.Parse(Env("MS2_LIVE_COMBAT_MOB", "0")), added = npcs;
                    GD.Print($"live: combat spawn: {(hud.SubmitChatLine("/npc " + mob) ? "sent" : "no chat window")}");
                    await until(() => npcs > added, 60 * 5); await frames(120);
                    Check("the mob is in the field", npcs > added);
                    Shot("attack_spawned.png");
                    int presses = 0, deadBefore = kills;
                    for (; presses < 30 && kills == deadBefore; presses++)
                    {
                        bool cast = hud.AttackScripted();
                        if (presses == 0) { Check("the attack key gave a cast request", cast); await frames(30); Shot("attack_cast.png"); }
                        await frames(60);
                        if (presses == 1) Shot("attack_hit.png");
                    }
                    await frames(120);
                    Check($"damage numbers came back ({numbers})", numbers > 0);
                    Check($"the kill reached the client ({kills} exp line(s), {presses} press(es))", kills > deadBefore);
                    Shot("attack_end.png");
                    break;
                }
                case "death":
                {
                    int mob = int.Parse(Env("MS2_LIVE_COMBAT_MOB", "0")), count = int.Parse(Env("MS2_LIVE_COMBAT_MOBS", "3"));
                    for (int i = 0; i < count; i++) { hud.SubmitChatLine("/npc " + mob); await frames(20); }
                    await until(() => live.Me.Dead, 60 * 90); await frames(180);
                    Check("the own character is dead", live.Me.Dead);
                    Check("the tombstone exists", live.Tomb != null);
                    Check("the revival dialog or the auto-revive countdown is up", hud.RevivalUiUp());
                    Shot("death.png");
                    break;
                }
                case "revive":
                {
                    Check("the revival dialog is open", hud.RevivalUiUp());
                    GD.Print($"live: combat free revive: {(hud.ReviveScripted(1) ? "sent" : "no dialog")}");
                    bool alive = false;     // seen at the revival itself: a mob that stands on the spot kills the revived character again
                    await until(() => alive = !live.Me.Dead, 60 * 15);
                    Check("the character is alive again", alive);
                    await frames(30);
                    Shot("revived.png");
                    break;
                }
                case "equip":
                {
                    int weapon = int.Parse(Env("MS2_LIVE_COMBAT_WEAPON", "0"));
                    GD.Print($"live: combat weapon item: {(hud.SubmitChatLine($"/item {weapon}") ? "sent" : "no chat window")}");
                    await until(() => live.Items.All.Any(e => e.Id == weapon), 60 * 10); await frames(60);
                    Check("the weapon is in the bag", live.Items.All.Any(e => e.Id == weapon));
                    GD.Print($"live: combat equip: {(hud.EquipScripted(weapon) ? "sent" : "not sent")}");
                    await until(() => live.Me.Equips?.Any(e => e.Id == weapon) == true, 60 * 10); await frames(60);
                    Check("the weapon is worn", live.Me.Equips?.Any(e => e.Id == weapon) == true);
                    Shot("equipped.png");
                    break;
                }
                case "revivepaid":
                {
                    Check("the revival dialog is open", hud.RevivalUiUp());
                    GD.Print($"live: combat paid revive: {(hud.ReviveScripted(2) ? "sent" : "no dialog")}");
                    bool alive = false;
                    await until(() => alive = !live.Me.Dead, 60 * 15);
                    Check("the character is alive again after the paid revive", alive);
                    await frames(30);
                    Shot("revived_paid.png");
                    break;
                }
                case "loot":
                {
                    int item = int.Parse(Env("MS2_LIVE_COMBAT_ITEM", "0")), meso = int.Parse(Env("MS2_LIVE_COMBAT_MESO", "500"));
                    int mesoItem = int.Parse(Env("MS2_LIVE_COMBAT_MESO_ITEM", "90000001"));
                    long bag0 = BagSum(live);
                    GD.Print($"live: combat drop item: {(hud.SubmitChatLine($"/item {item} --drop") ? "sent" : "no chat window")}");
                    await frames(120); Shot("loot_item_dropped.png");
                    live.PickupKeyHeld = true;      // the pickup key held (the key handler's flag)
                    await until(() => BagSum(live) != bag0, 60 * 10); await frames(60);
                    live.PickupKeyHeld = false;
                    Check($"the bag changed ({bag0} -> {BagSum(live)})", BagSum(live) != bag0);
                    Shot("loot_item_picked.png");
                    GD.Print($"live: combat drop meso: {(hud.SubmitChatLine($"/item {mesoItem} --amount {meso} --drop") ? "sent" : "no chat window")}");
                    await frames(120); Shot("loot_meso_dropped.png");
                    live.PickupKeyHeld = true;
                    await until(() => mesoDelta > 0, 60 * 10); await frames(60);
                    live.PickupKeyHeld = false;
                    Check($"the meso count changed ({mesoDelta})", mesoDelta > 0);
                    Shot("loot_meso_picked.png");
                    break;
                }
                default: GD.Print($"live: combat: unknown step {step}"); break;
            }
        }
    }
}
