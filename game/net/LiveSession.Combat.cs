using System;
using System.Threading.Tasks;
using Godot;
using Ms2Godot.Flow;
using Ms2Godot.World;

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
        int numbers = 0, kills = 0, npcs = 0, storeItem = int.Parse(Env("MS2_LIVE_STORE_ITEM", "0").Split(';')[0]);
        long mesoDelta = 0, lockUid = 0;
        int lockId = 0;
        var hitCount = new System.Collections.Generic.Dictionary<FieldNpcs.Placed, int>();
        live.DamageNumbered += (n, npc, pc) => { if (npc != null) { numbers++; hitCount[npc] = hitCount.GetValueOrDefault(npc) + 1; } };
        live.NpcExpShown += (npc, exp) => kills++;
        FieldNpcs.Placed lastNpc = null;
        live.NpcAdded += npc => { npcs++; lastNpc = npc; };
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
            if (step.StartsWith("chat:"))      // one chat line, no comma in it
            {
                GD.Print($"live: combat {step}: {(hud.SubmitChatLine(step[5..]) ? "sent" : "no chat window")}");
                await frames(120);
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
            if (step.StartsWith("store"))      // the bank steps: storeopen, storegive:N, storedeposit:N, storewithdraw:N, storemove, storemeso{deposit|withdraw}:N, storesort, storeclose
            {
                int sid = storeItem;
                long Bank() { long n = 0; foreach (var e in live.Storage.All) if (e.Id == sid) n += e.Amount; return n; }
                long Bag() { long n = 0; foreach (var e in live.Items.All) if (e.Id == sid) n += e.Amount; return n; }
                string arg = step.Contains(':') ? step[(step.IndexOf(':') + 1)..] : "";
                string name = step.Contains(':') ? step[..step.IndexOf(':')] : step;
                long bag0 = Bag(), bank0 = Bank(), meso0 = live.Storage.Meso;
                int slots0 = live.Storage.Count;
                switch (name)
                {
                    case "storeopen":
                    {
                        GD.Print("live: combat store: opener not exercised (the play opener does not exist on the scratch server)");
                        GD.Print($"live: combat store open: {(hud.StoreOpenScripted() ? "open" : "not open")}");
                        await until(() => live.Storage.Ready, 60 * 10); await frames(60);
                        Check($"the bank is loaded ({live.Storage.Count} item(s), {live.Storage.Meso} meso)", live.Storage.Ready);
                        Shot("store_open.png");
                        break;
                    }
                    case "storegive":
                    {
                        foreach (var cand in Env("MS2_LIVE_STORE_ITEM", "0").Split(';'))      // the first id the GM item command gives
                        {
                            storeItem = sid = int.Parse(cand); bag0 = Bag();
                            hud.SubmitChatLine($"/item {sid} --amount {arg}");
                            await until(() => Bag() != bag0, 60 * 3); await frames(30);
                            if (Bag() != bag0) break;
                        }
                        Check($"the bag holds the item ({bag0} -> {Bag()})", Bag() == bag0 + long.Parse(arg));
                        break;
                    }
                    case "storedeposit":
                    {
                        int n = int.Parse(arg);
                        var held = live.Items.All.FirstOrDefault(e => e.Id == sid);
                        GD.Print($"live: combat store deposit {n}: {(held != null && hud.StoreDepositScripted(held.Uid, n) ? "sent" : "not sent")}");
                        await until(() => Bank() != bank0, 60 * 10); await frames(60);
                        Check($"the bank holds the deposit ({bank0} -> {Bank()}), the bag lost it ({bag0} -> {Bag()})", Bank() == bank0 + n && Bag() == bag0 - n);
                        Shot("store_deposit.png");
                        break;
                    }
                    case "storewithdraw":
                    {
                        int n = int.Parse(arg);
                        int slot = -1; foreach (var e in live.Storage.All) if (e.Id == sid) { slot = e.InventorySlot; break; }
                        GD.Print($"live: combat store withdraw {n} from slot {slot}: {(slot >= 0 && hud.StoreWithdrawScripted(slot, n) ? "sent" : "not sent")}");
                        await until(() => Bank() != bank0, 60 * 10); await frames(60);
                        Check($"the bank lost the withdrawal ({bank0} -> {Bank()}), the bag has it ({bag0} -> {Bag()})", Bank() == bank0 - n && Bag() == bag0 + n);
                        Shot("store_withdraw.png");
                        break;
                    }
                    case "storemove":      // a bank slot dragged onto the first free one (the merge/split of the bank's own slots)
                    {
                        int from = -1; foreach (var e in live.Storage.All) if (e.Id == sid) { from = e.InventorySlot; break; }
                        int to = live.Storage.EmptySlot();
                        GD.Print($"live: combat store move {from} -> {to}: {(from >= 0 && hud.StoreMoveScripted(from, to) ? "sent" : "not sent")}");
                        await until(() => live.Storage.Slot(to) != null, 60 * 10); await frames(60);
                        Check($"the item moved to bank slot {to}", live.Storage.Slot(to) != null && live.Storage.Slot(from) == null);
                        Shot("store_move.png");
                        break;
                    }
                    case "storemesodeposit":
                    case "storemesowithdraw":
                    {
                        long n = long.Parse(arg); bool dep = name == "storemesodeposit";
                        long wallet0 = hud.WalletMeso;
                        GD.Print($"live: combat store meso {name} {n}: {(hud.StoreMesoScripted(dep, n) ? "sent" : "not sent")}");
                        await until(() => live.Storage.Meso != meso0, 60 * 10); await frames(60);
                        long wantBank = dep ? meso0 + n : meso0 - n;
                        Check($"the bank meso is {live.Storage.Meso} (wanted {wantBank})", live.Storage.Meso == wantBank);
                        Check($"the wallet is {hud.WalletMeso} (was {wallet0})", hud.WalletMeso == (dep ? wallet0 - n : wallet0 + n));
                        Shot("store_" + name + ".png");
                        break;
                    }
                    case "storesort":
                        GD.Print($"live: combat store sort: {(hud.StoreSortScripted() ? "sent" : "not sent")}");
                        await frames(240); Shot("store_sort.png");
                        Check($"the bank still has {live.Storage.Count} item(s) (was {slots0})", live.Storage.Count == slots0);
                        break;
                    case "storeclose":
                        GD.Print($"live: combat store close: {(hud.StoreCloseScripted() ? "sent" : "not open")}");
                        await frames(120); Shot("store_close.png");
                        break;
                    default: GD.Print($"live: combat: unknown step {step}"); break;
                }
                continue;
            }
            if (step.StartsWith("lock"))      // the item lock steps: lockgive, lockopen, lockselect, lockconfirm, lockcheck, lockunlock, lockclose
            {
                string lname = step;
                switch (lname)
                {
                    case "lockgive":
                    {
                        foreach (var cand in Env("MS2_LIVE_LOCK_ITEM", "0").Split(';'))      // the first id the GM item command gives
                        {
                            lockId = int.Parse(cand);
                            var known = live.Items.All.Select(e => e.Uid).ToHashSet();
                            hud.SubmitChatLine($"/item {lockId}");
                            await until(() => live.Items.All.Any(e => e.Id == lockId && !known.Contains(e.Uid)), 60 * 3); await frames(30);
                            if (live.Items.All.FirstOrDefault(e => e.Id == lockId && !known.Contains(e.Uid)) is { } got) { lockUid = got.Uid; break; }
                        }
                        Check($"the bag holds the item to lock (id {lockId})", lockUid != 0 && live.Items.Get(lockUid) != null);
                        break;
                    }
                    case "lockopen":
                        Check("the lock dialog is open", hud.LockOpenScripted());
                        await frames(90); Shot("lock_open.png");
                        break;
                    case "lockselect":
                    {
                        bool sent = hud.LockSelectScripted(lockUid);
                        GD.Print($"live: combat lock select: {(sent ? "sent" : "not sent")}");
                        await until(() => live.LockBook.Contains(lockUid), 60 * 10); await frames(60);
                        Check("the item is in the lock bag (the server's answer)", live.LockBook.Contains(lockUid));
                        Shot("lock_select.png");
                        break;
                    }
                    case "lockconfirm":
                    {
                        byte state0 = live.Items.Get(lockUid)?.LockState ?? 0; long at0 = live.Items.Get(lockUid)?.UnlockTime ?? 0;
                        GD.Print($"live: combat lock confirm: {(hud.LockConfirmScripted() ? "asked and answered yes" : "no box")}");
                        await until(() => !live.LockBook.Contains(lockUid), 60 * 10); await frames(60);
                        var r = hud.LockRulesScripted(lockUid);
                        GD.Print($"live: combat lock state: {state0} -> {r.State}, unlock time {at0} -> {r.UnlockAt}, server time {r.Now}");
                        Check("the item left the lock bag after the answer", !live.LockBook.Contains(lockUid));
                        Shot("lock_confirm.png");
                        break;
                    }
                    case "lockcheck":
                    {
                        var r = hud.LockRulesScripted(lockUid);
                        GD.Print($"live: combat lock rules: not locked={r.NotLocked} state={r.State} unlock time={r.UnlockAt} server time={r.Now}");
                        Check("the item is locked (a trade, a shop sale and a dismantle are refused by the lock rule)", !r.NotLocked);
                        break;
                    }
                    case "lockfree":
                    {
                        var r = hud.LockRulesScripted(lockUid);
                        GD.Print($"live: combat lock rules: not locked={r.NotLocked} state={r.State} unlock time={r.UnlockAt} server time={r.Now}");
                        Check("the item is free (the lock rule lets it through)", r.NotLocked);
                        break;
                    }
                    case "lockunlock":
                    {
                        Check("the unlock tab is up", hud.LockTabScripted(1));
                        GD.Print($"live: combat lock select (unlock): {(hud.LockSelectScripted(lockUid) ? "sent" : "not sent")}");
                        await until(() => live.LockBook.Contains(lockUid), 60 * 10); await frames(60);
                        Check("the item is in the unlock bag", live.LockBook.Contains(lockUid));
                        GD.Print($"live: combat lock unlock confirm: {(hud.LockConfirmScripted() ? "asked and answered yes" : "no box")}");
                        await until(() => !live.LockBook.Contains(lockUid), 60 * 10); await frames(60);
                        var r = hud.LockRulesScripted(lockUid);
                        GD.Print($"live: combat lock state after the unlock request: {r.State}, unlock time {r.UnlockAt}, server time {r.Now}, wait {r.UnlockAt - r.Now} s");
                        Shot("lock_unlock.png");
                        break;
                    }
                    case "lockclose":
                        hud.LockCloseScripted(); await frames(60); Shot("lock_close.png");
                        break;
                    default: GD.Print($"live: combat: unknown step {step}"); break;
                }
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
                    for (; presses < 30 && kills - deadBefore < npcs - added; presses++)
                    {
                        bool cast = hud.AttackScripted();
                        if (presses == 0) { Check("the attack key gave a cast request", cast); await frames(30); Shot("attack_cast.png"); }
                        await frames(20); if (presses < 3) Shot($"attack_press{presses}.png"); await frames(40);
                        if (presses == 1) Shot("attack_hit.png");
                    }
                    await frames(120);
                    Check($"damage numbers came back ({numbers})", numbers > 0);
                    Check($"the kill reached the client ({kills} exp line(s), {presses} press(es))", kills > deadBefore);
                    Shot("attack_end.png");
                    break;
                }
                case "aimattack":      // attack, the player turned to the mob before each press; hits (damage numbers) per press
                {
                    int mob = int.Parse(Env("MS2_LIVE_COMBAT_MOB", "0")), added = npcs;
                    hud.SubmitChatLine("/npc " + mob);
                    await until(() => npcs > added, 60 * 5); await frames(120);
                    Check("the mob is in the field", npcs > added && lastNpc != null);
                    int deadBefore = kills, hitPresses = 0, press = 0, need = int.Parse(Env("MS2_LIVE_COMBAT_PRESSES", "3"));
                    for (; press < 12 && press < need; press++)
                    {
                        if (kills > deadBefore)      // the mob died: a fresh one takes the next press, so the count is of presses at a live mob
                        {
                            deadBefore = kills; int again = npcs;
                            hud.SubmitChatLine("/npc " + mob);
                            await until(() => npcs > again, 60 * 5); await frames(120);
                        }
                        float dist = lastNpc?.Root != null && GodotObject.IsInstanceValid(lastNpc.Root) ? hud.FaceScripted(lastNpc.Position) : -1;
                        await frames(20);
                        var target = lastNpc;      // the number counts only when it lands on the mob the press aimed at
                        int before = hitCount.GetValueOrDefault(target);
                        hud.AttackScripted();
                        await until(() => hitCount.GetValueOrDefault(target) > before, 60 * 2);
                        bool hit = hitCount.GetValueOrDefault(target) > before;
                        if (hit) { hitPresses++; await frames(6); Shot($"aim_dmg_in{press}.png"); }
                        GD.Print($"live: combat aim press {press}: distance {dist:F0}, numbers on the mob {hitCount.GetValueOrDefault(target) - before}");
                        await frames(60);
                    }
                    Check($"enough presses to judge ({press}, need {need})", press >= need);
                    Check($"hits per press: {hitPresses} of {press}", press >= need && hitPresses == press);
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
                case "unequip":
                {
                    int weapon = int.Parse(Env("MS2_LIVE_COMBAT_WEAPON", "0"));
                    GD.Print($"live: combat unequip: {(hud.UnequipScripted(weapon) ? "sent" : "not worn")}");
                    await until(() => live.Me.Equips?.Any(e => e.Id == weapon) != true, 60 * 10); await frames(60);
                    Check("the weapon is off", live.Me.Equips?.Any(e => e.Id == weapon) != true);
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
