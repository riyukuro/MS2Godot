using System;
using System.Threading.Tasks;
using Godot;

namespace Ms2Godot.Net;

/// <summary>
/// The live run's own pet steps (MS2_LIVE_COMBAT_STEP): a pet item double clicked in the bag (MyPC::FUN_14152aa90's summon,
/// unsummon and transfer), and a walk with the pet following.
/// </summary>
public partial class LiveSession
{
    internal async Task<bool> PetStep(string step, Ui.HudScene hud, Func<int, Task> frames, Func<Func<bool>, int, Task> until,
        Action<string> shot, Action<string, bool> check)
    {
        if (step.StartsWith("petuse:"))      // petuse:<item id>:<summon|unsummon|transfer>: the item given when missing, its slot double clicked
        {
            var a = step[7..].Split(':');
            int id = int.Parse(a[0]);
            string expect = a.Length > 1 ? a[1] : "summon";
            long Uid() { foreach (var e in Items.All) if (e.Id == id) return e.Uid; return 0; }
            if (Uid() == 0) { hud.SubmitChatLine($"/item {id}"); await until(() => Uid() != 0, 60 * 5); await frames(30); }
            long uid = Uid();
            check($"pet item {id} in the bag", uid != 0);
            if (uid == 0) return true;
            hud.OpenInventoryScripted();
            await frames(30);
            long before = Pets.SummonUid;
            int petBefore = Me.PetObjectId;
            string slot = hud.BagSlotScripted(id, Items.TabOf(id));
            GD.Print($"live: pet use {id} uid {uid} ({slot}), summoned {before} pet object {petBefore}: {(hud.BagUseScripted(slot) ? "double clicked" : "no slot")}");
            await frames(30);
            if (hud.ShownBoxScripted() is { Extra: "summonBindPet" } box)     // the bind-on-use pet's box (UseMode 16's summonBindPet), its yes
            {
                GD.Print($"live: pet use {id}: bind box \"{box.Msg}\" shown");
                shot($"pet_bindbox_{id}.png");
                GD.Print($"live: pet use {id}: bind box yes {(hud.BagBoxAnswerScripted(box, 0, box.Extra) ? "answered" : "no box")}");
            }
            bool Done() => expect == "unsummon" ? Pets.SummonUid == 0 && Me.PetObjectId == 0
                : Pets.SummonUid == uid && Me.PetObjectId != 0 && Me.PetObjectId != petBefore && (remote?.HasNpc(Me.PetObjectId) ?? false);
            await until(Done, 60 * 8);
            await frames(90);
            GD.Print($"live: pet use {id} {expect}: summoned {Pets.SummonUid} pet object {Me.PetObjectId} placed {remote?.HasNpc(Me.PetObjectId) ?? false} " +
                     $"menu {(OwnPetMenuInfo() is { } m ? $"attack type {m.AttackType} auto {m.Auto}" : "none")}");
            hud.CloseInventoryScripted();
            await frames(30);
            shot($"pet_{expect}_{id}.png");
            check($"pet item {id}: {expect}", Done());
            return true;
        }
        if (step == "petesc" || step.StartsWith("petesc:"))   // petesc[:n]: Escape pressed n times (2 by default: a guide
        {                                                       // window the field entry opened closed)
            int presses = step.Length > 7 && int.TryParse(step.AsSpan(7), out int n) ? n : 2;
            for (int k = 0; k < presses; k++)
            {
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true });
                await frames(2);
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = false });
                await frames(20);
            }
            return true;
        }
        if (step.StartsWith("petwalk:"))     // petwalk:<left|right|up|down>:<frames>: the arrow key held, then the pet's move and its distance to the own pc
        {
            var a = step[8..].Split(':');
            var key = a[0] switch { "left" => Key.Left, "right" => Key.Right, "down" => Key.Down, _ => Key.Up };
            int hold = a.Length > 1 ? int.Parse(a[1]) : 120;
            if (player == null || !IsInstanceValid(player)) return true;
            int pet = Me.PetObjectId;
            var start = player.GlobalPosition;
            var petStart = remote?.NpcGodotPosition(pet);
            Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
            for (int f = 0; f < hold; f++) await frames(1);
            Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
            await frames(90);
            var end = player.GlobalPosition;
            var petEnd = remote?.NpcGodotPosition(pet);
            float walked = start.DistanceTo(end);
            float petMoved = petStart is { } ps && petEnd is { } pe ? ps.DistanceTo(pe) : -1;
            float gap = petEnd is { } p2 ? p2.DistanceTo(end) : -1;
            GD.Print($"live: pet walk {key} {hold} frames: pc moved {walked:0}, pet {pet} moved {petMoved:0}, gap {gap:0}, input block {Ms2.Ui.InputBlock.Active}");
            shot($"pet_follow_{a[0]}.png");
            check("the pet follows the own pc", walked > 50 && petMoved > 50 && gap >= 0 && gap < 600);
            return true;
        }
        return false;
    }
}
