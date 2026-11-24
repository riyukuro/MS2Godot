using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>PartyVoteCli's start and end outside the book: the chat lines, the sound, the ready check's box and its answer.</summary>
[NonParallelizable]
public class PartyVoteUiTests
{
    static byte[] Bytes(IOutPacket p)
    {
        var w = new ByteWriter();
        p.Write(w);
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }

    static PartyVote Ready(int id, params long[] yes)
    {
        var v = PartyVote.Create(PartyVote.Ready);
        v.Id = id;
        v.Participants.Add(1); v.Participants.Add(2);
        foreach (long x in yes) v.Yes.Add(x);
        return v;
    }

    [Test]
    public void TheReadyCheckAsksWithATimedBoxAndSendsTheAnswer()
    {
        var chat = new List<(string, int)>(); var sounds = new List<string>(); var sent = new List<byte[]>();
        var boxes = new List<MessageBoxData>(); var notices = new List<string>();
        var dialog = new PartyController();
        dialog.SetDeadline(5);
        var ui = new PartyVoteUi
        {
            SelfCharacterId = 1,
            Dialog = () => dialog,
            ChatLine = (t, k) => chat.Add((t, k)),
            PlaySound = sounds.Add,
            ShowBox = boxes.Add,
            Notice = notices.Add,
            Send = p => sent.Add(Bytes(p)),
            NowMs = () => 1000,
        };
        var book = new PartyBook();
        ui.OnStarted(book, Ready(7));
        Assert.That(dialog.Deadline, Is.Zero, "SetDeadline(0)");
        Assert.That((chat.Single().Item2, sounds.Single()), Is.EqualTo((2, "System_PartyReady_Popup_01")));
        var box = boxes.Single();
        Assert.That((box.Type, box.HasTimer, box.Timer.Sec, box.Timer.ShowFormat), Is.EqualTo((1, true, 0x14, 1)));
        Assert.That((box.NoInputBlock, box.Modaless, box.EnterAllowed), Is.EqualTo((true, true, false)));
        // OnMessageBoxResult: 0 yes; another button no; a button the timer pressed sends nothing
        Assert.That(box.InvokeCallback(0, null), Is.True);
        Assert.That(box.InvokeCallback(1, null), Is.True);
        box.Timer.Button = 1;
        box.InvokeCallback(1, null);
        Assert.That(sent, Is.EqualTo(new[] { new byte[] { 0x30, 7, 0, 0, 0, 1 }, new byte[] { 0x30, 7, 0, 0, 0, 0 } }));
        // already answered: no box
        ui.OnStarted(book, Ready(8, 1));
        Assert.That(boxes, Has.Count.EqualTo(1));
        // vfunc_3: the notice, and the ready marks kept for PartyVoteReadyTagExpireSeconds (10 by default)
        ui.OnEnded(book, Ready(8, 1));
        Assert.That((notices.Count, dialog.Deadline), Is.EqualTo((1, 11000L)));
    }

    [Test]
    public void TheKickVotePostsItsChatLineOnly()
    {
        var chat = new List<(string, int)>(); var boxes = new List<MessageBoxData>(); var notices = new List<string>();
        var ui = new PartyVoteUi { ChatLine = (t, k) => chat.Add((t, k)), ShowBox = boxes.Add, Notice = notices.Add, PlaySound = _ => Assert.Fail("no sound") };
        var v = PartyVote.Create(PartyVote.KickUser);
        v.Owner = 3;
        ui.OnStarted(new PartyBook(), v);
        ui.OnEnded(new PartyBook(), v);
        Assert.That((chat.Single().Item2, boxes.Count, notices.Count), Is.EqualTo((2, 0, 0)));
    }

    /// <summary>The vote page's title and timer are StringFormat texts with "{0:02d}" style fields; none may reach the page raw.</summary>
    [Test]
    public void TheKickVoteTitleAndTimerFillTheirSpecFields()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var tables = new Ms2.Ui.GameTables(new Ms2.Formats.Pack.PackSet(d), "en");
        var v = PartyVote.Create(PartyVote.KickUser);
        v.Name = "Target"; v.Reason = 2; v.Expires = 1000 + 65;
        Assert.That(PartyController.RemainingTimeText(tables, v.Expires, 1000), Does.Not.Contain("{").And.Contain("01").And.Contain("05"));
        string title = PartyController.VoteTitle(tables, v, 1000);
        Assert.That(title, Does.Not.Contain("{").And.Not.Contain("}").And.Contain("Target"));
    }
}
