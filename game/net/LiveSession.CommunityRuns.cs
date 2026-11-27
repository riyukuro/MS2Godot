using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Ms2Godot.World;

namespace Ms2Godot.Net;

/// <summary>
/// The live run's friend and community steps (MS2_LIVE_COMBAT_STEP): the popup text command, raw GmCommand,
/// MessengerBrowserStamp, BuddyBadge and Buddy packets the server's debug packet command sends (no server sends them), and the
/// friend request, accept, decline, cancel, delete, block, memo and unblock through the friend dialogs' own callbacks.
/// </summary>
public partial class LiveSession
{
    int buddyPackets;       // Buddy packets read, for the live steps
    /// <summary>A scripted run with steps (MS2_LIVE_COMBAT_STEP): only then are the sends and protocols read kept as text
    /// and a list for the steps, so a normal session allocates nothing for them.</summary>
    static readonly bool CommunitySteps = !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("MS2_LIVE_COMBAT_STEP"));

    static void I64(List<byte> b, long v) { I32(b, (int)v); I32(b, (int)(v >> 32)); }

    /// <summary>A raw GmCommand mode 1 (OnGmCommand's layout): the address, the text and the third string.</summary>
    internal string GmPopupTextBytes(string url, string text)
    {
        var b = new List<byte>();
        Op(b, game.Profile.Number(Ms2.Net.Inbound.GmCommand)); b.Add(Ms2.Net.Packets.GmCommand.PopupText);
        Unicode(b, url); Unicode(b, text); Unicode(b, "");
        return Hex(b);
    }

    /// <summary>A raw MessengerBrowserStamp (OnMessengerBrowserStamp's layout): the count, then each chat type as a u16.</summary>
    internal string BrowserStampBytes(IReadOnlyList<int> types)
    {
        var b = new List<byte>();
        Op(b, game.Profile.Number(Ms2.Net.Inbound.MessengerBrowserStamp)); I32(b, types.Count);
        foreach (int t in types) { b.Add((byte)t); b.Add((byte)(t >> 8)); }
        return Hex(b);
    }

    /// <summary>A raw BuddyBadge (OnBuddyBadge's layout): the kind and the character id, kind 3 the item uid and the name.</summary>
    internal string BuddyBadgeBytes(byte kind, long characterId, long itemUid = 0, string name = "")
    {
        var b = new List<byte>();
        Op(b, game.Profile.Number(Ms2.Net.Inbound.BuddyBadge)); b.Add(kind); I64(b, characterId);
        if (kind == Ms2.Net.Packets.BuddyBadgeMessage.PartnerName) { I64(b, itemUid); Unicode(b, name); }
        return Hex(b);
    }

    /// <summary>One friend or community step; false when the step is not one of them.</summary>
    internal async Task<bool> CommunityStep(string step, Ui.HudScene hud, Func<int, Task> frames, Func<Func<bool>, int, Task> until,
        Action<string> shot, Action<string, bool> check)
    {
        string Popup() => hud.CommunityPopup is { } p ? $"open {p.IsOpen}, movie {hud.CommunityPopupUp}, line \"{p.Current.Text.TrimEnd()}\", url \"{p.Current.Url}\", queued {p.Queued}, x {p.X:F0}, width {p.TextWidth}" : "no popup";
        if (step.StartsWith("popuptext:"))      // popuptext:<word>: the PopupText user command with that word and an address; the ticker's state now and 3 s later
        {
            bool sent = hud.SubmitChatLine($"/PopupText {step[10..]} https://example.com/");
            await until(() => hud.CommunityPopupUp, 60 * 5); await frames(30);
            GD.Print($"live: community {step}: {Popup()} (sent {sent})");
            float x0 = hud.CommunityPopup?.X ?? 0;
            shot($"popuptext_{step[10..]}.png");
            await frames(180);
            GD.Print($"live: community {step} 3 s later: {Popup()}");
            check($"popup text {step[10..]} is up and scrolls", hud.CommunityPopupUp && hud.CommunityPopup.Current.Text.TrimEnd() == step[10..] && hud.CommunityPopup.X != x0);
            shot($"popuptext_{step[10..]}_3s.png");
            return true;
        }
        if (step == "popupclick")      // the ticker's click: the browser opens the address and the line ends at the next tick
        {
            hud.CommunityPopup?.OnClick();
            await frames(60);
            GD.Print($"live: community popupclick: {Popup()}");
            check("the clicked line ended", hud.CommunityPopup is { } p && (p.Current.Text.Length == 0 || !p.IsOpen));
            return true;
        }
        if (step.StartsWith("gmcmdraw:"))      // gmcmdraw:<word>: a raw GmCommand mode 1; what was read and what the ticker shows (feature 0x88 gates it)
        {
            int before = GmCommands;
            string line = hud.CommunityPopup?.Current.Text ?? "";
            bool sent = hud.SubmitChatLine("/debug packet " + GmPopupTextBytes("https://example.com/", step[9..]));
            await until(() => GmCommands > before, 60 * 5); await frames(60);
            bool feature = PopupTextFeature();
            GD.Print($"live: community {step}: read \"{LastGmCommand}\", feature 0x88 {(feature ? "on" : "off")}, {Popup()} (sent {sent})");
            bool shown = hud.CommunityPopup?.Current.Text.TrimEnd() == step[9..] || (hud.CommunityPopup?.Queued ?? 0) > 0 && line.Length > 0;
            check($"gm command read ({GmCommands - before}) and {(feature ? "shown" : "not shown")}", GmCommands > before && shown == feature);
            shot($"gmcmd_{step[9..]}.png");
            return true;
        }
        if (step.StartsWith("stampraw:"))      // stampraw:<type>/<type>...: a raw MessengerBrowserStamp; the chat windows' hidden types after
        {
            var types = step[9..].Split('/', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
            bool sent = hud.SubmitChatLine("/debug packet " + BrowserStampBytes(types));
            var hidden = Ui.HudScene.ChatLog.HiddenTypes;
            await until(() => hidden.SetEquals(types), 60 * 5); await frames(30);
            GD.Print($"live: community {step}: hidden [{string.Join(",", hidden.OrderBy(t => t))}] (sent {sent})");
            check($"stamp hides [{step[9..]}]", hidden.SetEquals(types));
            return true;
        }
        if (step.StartsWith("badgeraw:"))      // badgeraw:<kind>: a raw BuddyBadge for the own character (kind 3: a made up item uid and name); the PCs' couple line after
        {
            byte kind = byte.Parse(step[9..]);
            string before = NpcEmoticons.Live?.CoupleLine() ?? "";
            int got = 0; void On(Ms2.Net.Packets.BuddyBadgeMessage m) => got++;
            BuddyBadgeReceived += On;
            bool sent = hud.SubmitChatLine("/debug packet " + BuddyBadgeBytes(kind, CharacterId, 1, "Partner"));
            await until(() => got > 0, 60 * 5); await frames(30);
            BuddyBadgeReceived -= On;
            string after = NpcEmoticons.Live?.CoupleLine() ?? "";
            GD.Print($"live: community {step}: received {got}, {LastBuddyBadgeIn}; before {before}; after {after} (sent {sent})");
            check($"buddy badge kind {kind} read", got > 0);
            return true;
        }
        if (step == "communityopen")      // the friend list toggled open, with a shot
        {
            bool open = hud.CommunityOpenScripted || hud.ToggleCommunityScripted();
            await frames(90);
            GD.Print($"live: community open {open}: {BuddyLine()}");
            shot("community.png");
            return true;
        }
        if (step.StartsWith("buddyinvite:"))      // buddyinvite:<name>: the list's add button, the query dialog's OK with the name; waits for the server's answer
        {
            string name = step[12..];
            if (!hud.CommunityOpenScripted) hud.ToggleCommunityScripted();
            await frames(60);
            bool add = hud.CommunityCallbackScripted("onaddbuddy");
            await until(() => hud.CommunityQueryOpenScripted, 60 * 5); await frames(60);
            shot($"buddyquery_{name}.png");
            int before = buddyPackets;
            bool ok = hud.CommunityQueryOkScripted(1, name, "");
            await until(() => buddyPackets > before && Buddies.FindByName(name) != null, 60 * 5); await frames(60);
            var b = Buddies.FindByName(name);
            GD.Print($"live: community {step}: add {add}, ok {ok}, {buddyPackets - before} packets, query {(hud.CommunityQueryOpenScripted ? "still up" : "closed")}; {BuddyLine()}");
            check($"request to {name} is waiting", b is { Waiting: true });
            shot($"buddyinvite_{name}.png");
            return true;
        }
        if (step.StartsWith("buddyaccept:") || step.StartsWith("buddydecline:") || step.StartsWith("buddydelete:") || step.StartsWith("buddycancel:"))
        {
            // buddyaccept:<name> / buddydecline:<name> / buddydelete:<name> / buddycancel:<name>: the list's accept, decline,
            // delete or cancel callback for the entry of that name (the entry id as the movie passes it); waits for the book
            int colon = step.IndexOf(':');
            string kind = step[5..colon], name = step[(colon + 1)..];
            if (!hud.CommunityOpenScripted) hud.ToggleCommunityScripted();
            await until(() => Buddies.FindByName(name) != null, 60 * 10); await frames(60);
            var b = Buddies.FindByName(name);
            if (b == null) { check($"{kind} {name}: no entry", false); return true; }
            int before = buddyPackets;
            bool sent = hud.CommunityCallbackScripted($"on{kind}buddy", ((ulong)b.Id).ToString());
            bool Done() => kind == "accept" ? Buddies.FindByName(name) is { IsFriend: true } : Buddies.FindByName(name) == null;
            await until(() => buddyPackets > before && Done(), 60 * 5); await frames(90);
            GD.Print($"live: community {step}: sent {sent}, {buddyPackets - before} packets {Seen(before)}; {BuddyLine()}");
            check($"{kind} {name}", Done());
            shot($"buddy{kind}_{name}.png");
            return true;
        }
        if (step.StartsWith("buddyblock:"))      // buddyblock:<name>: the list's block button, the query's OK in its block mode, the box's yes
        {
            string name = step[11..];
            if (!hud.CommunityOpenScripted) hud.ToggleCommunityScripted();
            await frames(60);
            bool add = hud.CommunityCallbackScripted("onaddban");
            await until(() => hud.CommunityQueryOpenScripted, 60 * 5); await frames(60);
            int before = buddyPackets;
            bool ok = hud.CommunityQueryOkScripted(2, name, "memo");
            bool boxed = await AnswerBox(until, frames);
            await until(() => Buddies.FindByName(name) is { Blocked: true }, 60 * 5); await frames(60);
            GD.Print($"live: community {step}: add {add}, ok {ok}, box {boxed}, {buddyPackets - before} packets {Seen(before)}; {BuddyLine()}");
            check($"{name} is blocked", Buddies.FindByName(name) is { Blocked: true });
            shot($"buddyblock_{name}.png");
            return true;
        }
        if (step.StartsWith("buddymemo:"))      // buddymemo:<name>: the blocked tab, then its memo edit with a new memo
        {
            string name = step[10..], memo = "edited";
            if (!hud.CommunityOpenScripted) hud.ToggleCommunityScripted();
            var b = Buddies.FindByName(name);
            if (b == null) { check($"memo {name}: no entry", false); return true; }
            bool tab = hud.CommunityCallbackScripted("onchangetab", 3);
            await frames(30);
            int before = buddyPackets;
            bool sent = hud.CommunityCallbackScripted("oneditbanmemo", name, memo, ((ulong)b.Id).ToString());
            await until(() => buddyPackets > before && Buddies.FindByName(name)?.Memo == memo, 60 * 5); await frames(60);
            GD.Print($"live: community {step}: tab {tab}, sent {sent}, {buddyPackets - before} packets {Seen(before)}, memo \"{Buddies.FindByName(name)?.Memo}\"");
            check($"{name}'s block memo changed", Buddies.FindByName(name)?.Memo == memo);
            shot($"buddymemo_{name}.png");
            return true;
        }
        if (step.StartsWith("buddyunblock:"))      // buddyunblock:<name>: the popup menu's unblock item (the query's mode 7), the box's yes
        {
            string name = step[13..];
            var b = Buddies.FindByName(name);
            if (b == null) { check($"unblock {name}: no entry", false); return true; }
            int before = buddyPackets;
            bool q = hud.CommunityUnblockScripted(name, b.Id, b.CharacterId);
            bool boxed = await AnswerBox(until, frames);
            await until(() => Buddies.FindByName(name) == null, 60 * 5); await frames(60);
            GD.Print($"live: community {step}: query {q}, box {boxed}, {buddyPackets - before} packets {Seen(before)}; {BuddyLine()}");
            check($"{name} is unblocked", Buddies.FindByName(name) == null);
            return true;
        }
        if (step.StartsWith("buddyraw:"))      // buddyraw:<protocol>: a raw Buddy packet the server's debug packet command sends; what the book did
        {
            byte proto = byte.Parse(step[9..]);
            var friend = Buddies.Buddies.FirstOrDefault(x => x.IsFriend) ?? Buddies.Buddies.FirstOrDefault();
            int before = buddyPackets, sends = BuddySends, chats = 0, boxes = 0;
            void Chat(string text, int kind) => chats++;
            void Box(string text) => boxes++;
            Buddies.ChatLine += Chat; Buddies.Box += Box;
            bool sent = hud.SubmitChatLine("/debug packet " + BuddyBytes(proto, friend));
            await until(() => buddyPackets > before, 60 * 5); await frames(60);
            Buddies.ChatLine -= Chat; Buddies.Box -= Box;
            bool read = buddySeen.Skip(before).Contains(proto);
            bool shown = Ui.MessageBoxLayer.Instance?.Shown == true;
            GD.Print($"live: community {step}: read {read} {Seen(before)}, {chats} chat lines, {boxes} boxes (shown {shown}), {BuddySends - sends} sends (last {LastBuddySent}) (sent {sent}); {BuddyLine()}");
            bool did = proto switch
            {
                18 => boxes == 1 && shown,
                20 => BuddySends > sends && LastBuddySent.Contains("AutoReject"),
                0xb or 0xc or 0xd or 0xe => chats > 0,
                _ => true,
            };
            check($"buddy protocol {proto} read and handled", read && did);
            if (proto == 18) { shot("buddyraw_18.png"); await AnswerBox(until, frames); }
            return true;
        }
        if (step == "buddyseen")      // every Buddy protocol this run read, in order
        {
            GD.Print($"live: community buddyseen: [{string.Join(",", buddySeen)}], distinct [{string.Join(",", buddySeen.Distinct().OrderBy(x => x))}], {BuddySends} sends");
            return true;
        }
        return false;
    }

    /// <summary>A shown box's first button (yes / OK); false when no box came up.</summary>
    static async Task<bool> AnswerBox(Func<Func<bool>, int, Task> until, Func<int, Task> frames)
    {
        await until(() => Ui.MessageBoxLayer.Instance?.Shown == true, 60 * 5); await frames(30);
        if (Ui.MessageBoxLayer.Instance?.Shown != true) return false;
        Ui.MessageBoxLayer.Instance.Click(0);
        await frames(30);
        return true;
    }

    /// <summary>
    /// A raw Buddy packet (OnBuddy's layouts) for the protocols no server sends on its own: 11 (our request was accepted: the
    /// entry id), 12 (a block result and name), 13 (one answer "approved" while away), 14 (online flag 0, the id and name),
    /// 18 and 20 (no body, one pad byte). <paramref name="b"/> is the entry the packet names (none: id 0, an empty name).
    /// </summary>
    internal string BuddyBytes(byte proto, Ms2.Net.Packets.BuddyInfo b)
    {
        var o = new List<byte>();
        long id = b?.Id ?? 0; string name = b?.Name ?? "";
        Op(o, game.Profile.Number(Ms2.Net.Inbound.Buddy)); o.Add(proto);
        switch (proto)
        {
            case 0xb: I64(o, id); break;
            case 0xc: o.Add(0); Unicode(o, name); break;
            case 0xd: I32(o, 1); Unicode(o, name); Unicode(o, "approved"); I64(o, id); break;
            case 0xe: o.Add(0); I64(o, id); Unicode(o, name); break;
            // the debug packet command takes 4 bytes at least; OnBuddy reads no body for 18 and 20 and checks no end there
            case 18: case 20: o.Add(0); break;
        }
        return Hex(o);
    }

    readonly List<int> buddySeen = new();       // every Buddy protocol read, in order
    /// <summary>The friend dialogs' sends and the last one, for the live checks.</summary>
    public int BuddySends { get; private set; }
    public string LastBuddySent { get; private set; } = "";

    string Seen(int from) => $"[{string.Join(",", buddySeen.Skip(from))}]";

    /// <summary>The friend book's entries, for the live checks.</summary>
    string BuddyLine() => string.Join(" ", Buddies.Buddies.Select(b =>
        $"[{b.Name} id={b.Id} friend={b.IsFriend} received={b.Received} waiting={b.Waiting} blocked={b.Blocked} online={b.Online}]"));
}
