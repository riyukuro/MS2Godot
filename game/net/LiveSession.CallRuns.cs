using Godot;
using Ms2.Net.Packets;

namespace Ms2Godot.Net;

/// <summary>
/// The recall's and the air taxi's scripted chat-run lines (MS2_LIVE_CHAT), for a run with two characters:
/// "@partyinvite:&lt;name&gt;" asks that character into the party (the request the popup menu's invite sends) and waits for the
/// party (at most 60 s); "@partyaccept" waits for the invite box (at most 120 s), presses its accept and waits for the party;
/// "@partyrecall" presses the party window's recall button and waits for the cast to end (at most 15 s);
/// "@recallwait" waits for a recall notice (at most 120 s); "@recallclick" clicks it and waits for the confirm box;
/// "@recallyes" presses the box's yes and waits for a field change (at most 30 s);
/// "@airtaxi:&lt;field&gt;" opens the world map and asks for the air taxi to that field (the movie's onCashTaxi), then waits
/// for a box (at most 5 s); "@boxyes" presses the shown box's first button; "@cashcall" waits for the air taxi call
/// (at most 5 s), then for a field change (at most 40 s).
/// </summary>
public partial class LiveSession
{
    /// <summary>One chat-run line if it is a recall line (true), else false and nothing is done.</summary>
    static async Task<bool> CallChatStep(string line, Ui.HudScene hud, int n, Func<int, Task> frames, Func<Func<bool>, int, Task> until)
    {
        if (line.StartsWith("@partyinvite:"))
        {
            string name = line.Substring("@partyinvite:".Length);
            Instance?.Send(new PartyInviteRequest(name));
            GD.Print($"live: party invite {n}: {name} asked");
            await until(() => Instance == null || Instance.Party.IsInParty, 60 * 60);
            GD.Print($"live: party invite {n}: {(Instance?.Party.IsInParty == true ? $"in a party of {Instance.Party.Members.Count}" : "no party")}");
            await frames(120);
            return true;
        }
        if (line == "@partyaccept")
        {
            await until(() => Ui.MessageBoxLayer.Instance?.Shown == true, 60 * 120);
            bool shown = Ui.MessageBoxLayer.Instance?.Shown == true;
            GD.Print($"live: party accept {n}: {(shown ? "a box is shown" : "no box came")}");
            if (shown) { await frames(60); Ui.MessageBoxLayer.Instance.Click(0); }
            await until(() => Instance == null || Instance.Party.IsInParty, 60 * 10);
            GD.Print($"live: party accept {n}: {(Instance?.Party.IsInParty == true ? $"in a party of {Instance.Party.Members.Count}" : "no party")}");
            await frames(120);
            return true;
        }
        if (line == "@partyrecall")
        {
            GD.Print($"live: party recall {n}: button {(hud.PressPartyRecall() ? "pressed" : "not pressed (no window)")}");
            await frames(30);
            bool cast = Instance?.RecallCast.IsCasting == true;
            GD.Print($"live: party recall {n}: {(cast ? "the cast runs" : "no cast")}");
            await until(() => Instance == null || !Instance.RecallCast.IsCasting, 60 * 15);
            GD.Print($"live: party recall {n}: cast {(Instance?.RecallCast.IsCasting == true ? "still runs" : "ended")}");
            await frames(120);
            return true;
        }
        if (line == "@recallwait")
        {
            await until(() => hud.RecallNoticeSlot() >= 0, 60 * 120);
            GD.Print($"live: recall wait {n}: notice slot {hud.RecallNoticeSlot()}");
            await frames(120);
            return true;
        }
        if (line == "@recallclick")
        {
            int slot = hud.RecallNoticeSlot();
            GD.Print($"live: recall click {n}: slot {slot}, clicked {slot >= 0 && hud.ClickRecallNotice(slot)}");
            await until(() => Ui.MessageBoxLayer.Instance?.Shown == true, 60 * 5);
            GD.Print($"live: recall click {n}: {(Ui.MessageBoxLayer.Instance?.Shown == true ? "the confirm box is shown" : "no box")}");
            await frames(60);
            return true;
        }
        if (line == "@recallyes")
        {
            int before = Instance?.FieldId ?? 0;
            Ui.MessageBoxLayer.Instance?.Click(0);
            await until(() => Instance == null || (Instance.FieldId ?? 0) != before, 60 * 30);
            GD.Print($"live: recall yes {n}: field {before} -> {Instance?.FieldId ?? 0}");
            await frames(240);
            return true;
        }
        if (line.StartsWith("@airtaxi:") && int.TryParse(line.AsSpan("@airtaxi:".Length), out int to))
        {
            GD.Print($"live: air taxi {n}: to {to}, request {(hud.AirTaxiTo(to) ? "sent to the world map" : "not sent (no map)")}");
            await until(() => Ui.MessageBoxLayer.Instance?.Shown == true, 60 * 5);
            GD.Print($"live: air taxi {n}: {(Ui.MessageBoxLayer.Instance?.Shown == true ? "a box is shown" : "no box")}");
            await frames(60);
            return true;
        }
        if (line == "@boxyes")
        {
            bool shown = Ui.MessageBoxLayer.Instance?.Shown == true;
            if (shown) Ui.MessageBoxLayer.Instance.Click(0);
            GD.Print($"live: box yes {n}: {(shown ? "pressed" : "no box")}");
            await frames(30);
            return true;
        }
        if (line == "@cashcall")
        {
            int before = Instance?.FieldId ?? 0;
            await until(() => Instance == null || Instance.CashCalling, 60 * 5);
            GD.Print($"live: cash call {n}: {(Instance?.CashCalling == true ? $"runs, kind {Instance.cashCall.Kind}" : "did not start")}");
            await until(() => Instance == null || (Instance.FieldId ?? 0) != before, 60 * 40);
            GD.Print($"live: cash call {n}: field {before} -> {Instance?.FieldId ?? 0}");
            await frames(240);
            return true;
        }
        return false;
    }
}
