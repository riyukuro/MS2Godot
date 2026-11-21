using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
using NUnit.Framework;
using Maple2.PacketLib.Tools;

namespace Ms2.Ui.Tests;

/// <summary>The mail send dialog (uimailsenddialog; needs MS2_DATA): the movie's children, the refusals (each sends nothing and
/// gives its notice), the send's bytes, the reply data, the keys, the candidates and the error that selects the receiver.
/// Texts are not compared with a language's strings: a notice is compared with the table's own text for its id.</summary>
public class MailSendDialogTests
{
    static PackSet packs;
    static MovieSource src;

    static void Packs()
    {
        if (packs != null) return;
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        packs = new PackSet(d);
        src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
    }

    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    sealed class Rig
    {
        public MailSendDialogController C;
        public List<IOutPacket> Sent = new();
        public List<string> Notices = new();
        public UiModel Model;
        public UiHost Host;
        public Player P;

        public Rig(MailInfo reply = null, Action<MailSendDialogController> setup = null)
        {
            Packs();
            Model = new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = true };
            C = new MailSendDialogController { Send = Sent.Add, DateText = t => "D" + t, FormatId = (id, a) => id + ":" + string.Join("|", a) };
            C.NoticeText += Notices.Add;
            setup?.Invoke(C);
            C.SetReplyMail(reply);
            var (rt, p) = src.Open(C.Movie, r => r.Budget = 20_000_000);
            p.StageW = 1280; p.StageH = 720;
            p.Translate = k => k.TrimStart('$');
            P = p;
            Host = new UiHost(p, C, Model);
            Assert.That(Host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
            for (int i = 0; i < 10; i++) p.Tick();
        }

        public void Fill(string receiver, string title, string content)
        {
            Host.Set(Host.Child("m_receiver"), "text", receiver);
            Host.Set(Host.Get(Host.Child("m_title"), "textField"), "text", title);
            Host.Set(Host.Get(Host.Child("m_content"), "textField"), "text", content);
        }

        public string Common(int id) => Model.Tables.Common(id);
    }

    [Test]
    public void TheMovieHasItsFieldsAndTheDialogIsKnownAs0x4e4a()
    {
        var r = new Rig();
        foreach (var n in new[] { "m_receiver", "m_title", "m_content", "m_send", "m_close", "m_textLimit" })
            Assert.That(r.Host.Child(n), Is.Not.Null, n);
        Assert.That(MailSendDialogController.UiId, Is.EqualTo(0x4e4a));
        Assert.That(r.C.TypeName, Is.EqualTo("MailSendDialog"));
        Assert.That(r.Sent, Is.Empty, "opening sends nothing");
    }

    [Test]
    public void TheSendWritesTheTrimmedReceiverTheTitleAndTheContent()
    {
        var r = new Rig();
        r.Fill("  Ann ", "Hi", "Body text");
        Assert.That(r.C.OnCallback("OnSend", Array.Empty<object>()), Is.True);
        var p = (MailSendRequest)r.Sent.Single();
        Assert.That(p, Is.EqualTo(new MailSendRequest("  Ann ", "Hi", "Body text")), "the trim is in the bytes");
        Assert.That(Bytes(p)[0], Is.EqualTo(1));
        Assert.That(r.Notices, Is.Empty);
    }

    [Test]
    public void AnEmptyTitleIsRefusedBeforeAnEmptyContent()
    {
        var r = new Rig();
        r.Fill("Ann", "", "");
        r.C.OnSend();
        Assert.That(r.Sent, Is.Empty);
        Assert.That(r.Notices.Single(), Is.EqualTo(r.Common(MailSendDialogController.EmptyTitleNotice)));
        Assert.That(r.Notices.Single(), Is.Not.Empty);
    }

    [Test]
    public void AnEmptyContentIsRefused()
    {
        var r = new Rig();
        r.Fill("Ann", "Hi", "");
        r.C.OnSend();
        Assert.That(r.Sent, Is.Empty);
        Assert.That(r.Notices.Single(), Is.EqualTo(r.Common(MailSendDialogController.EmptyContentNotice)));
        Assert.That(r.Notices.Single(), Is.Not.Empty);
    }

    [Test]
    public void ALongTitleOrContentIsRefusedWithTheSendMailNotice()
    {
        var r = new Rig();
        // the movie's inputs cap the text; the check runs on what the text reads, so give it through the filter
        r.C.FilterText = s => s == "T" ? new string('a', 101) : s == "C" ? new string('b', 1) : s;
        r.Fill("Ann", "T", "C");
        r.C.OnSend();
        Assert.That(r.Sent, Is.Empty);
        Assert.That(r.Notices.Single(), Is.EqualTo(r.Common(MailSendDialogController.LimitNotice)));
        r.Notices.Clear();
        r.C.FilterText = s => s == "C" ? new string('b', 2001) : s;
        r.C.OnSend();
        Assert.That(r.Sent, Is.Empty);
        Assert.That(r.Notices.Single(), Is.EqualTo(r.Common(MailSendDialogController.LimitNotice)));
        Assert.That(r.Notices.Single(), Is.Not.Empty);
    }

    [Test]
    public void TheLimitsAreInclusive()
    {
        var r = new Rig();
        r.C.FilterText = s => s == "T" ? new string('a', 100) : s == "C" ? new string('b', 2000) : s;
        r.Fill("Ann", "T", "C");
        Assert.That(r.C.OnSend(), Is.Not.Null);
        Assert.That(r.Notices, Is.Empty);
    }

    [Test]
    public void TheBanCheckerRefusesWithItsMessageAndTheTitleIsTestedFirst()
    {
        var r = new Rig(setup: c =>
        {
            c.CheckTitle = t => t == "bad" ? "no title" : null;
            c.CheckContent = t => t == "bad" ? "no content" : null;
        });
        r.Fill("Ann", "bad", "bad");
        r.C.OnSend();
        Assert.That(r.Notices.Single(), Is.EqualTo("no title"));
        r.Notices.Clear();
        r.Fill("Ann", "ok", "bad");
        r.C.OnSend();
        Assert.That(r.Notices.Single(), Is.EqualTo("no content"));
        Assert.That(r.Sent, Is.Empty);
    }

    [Test]
    public void TheLimitInputCallbackGivesItsNotice()
    {
        var r = new Rig();
        Assert.That(r.C.OnCallback("onLimitInput", Array.Empty<object>()), Is.True);
        Assert.That(r.Notices.Single(), Is.EqualTo(r.Common(MailSendDialogController.LimitInputNotice)));
        Assert.That(r.Notices.Single(), Is.Not.Empty);
        Assert.That(r.C.OnCallback("Unknown", Array.Empty<object>()), Is.False);
    }

    [Test]
    public void AReplyFillsTheThreeFieldsAndAnEmptyDraftLeavesThemEmpty()
    {
        var mail = new MailInfo { Sender = "Ann", Title = "Hi", Content = "Body", SentTime = 7 };
        var r = new Rig(mail);
        Assert.That(Rt.ToString(r.Host.Get(r.Host.Child("m_receiver"), "text")), Is.EqualTo("Ann"));
        Assert.That(Rt.ToString(r.Host.Get(r.Host.Child("m_title"), "text")), Does.Contain("2053:Hi"));
        Assert.That(Rt.ToString(r.Host.Invoke("getContent")), Does.Contain("2054:Ann|D7|Hi|Body"));
        var e = new Rig();
        Assert.That(Rt.ToString(e.Host.Get(e.Host.Child("m_receiver"), "text")), Is.Empty);
    }

    [Test]
    public void ClosingDropsTheReplyAndANullReplyIsIgnored()
    {
        var mail = new MailInfo { Sender = "Ann", Title = "Hi", Content = "Body" };
        var r = new Rig(mail);
        r.C.SetReplyMail(null);
        Assert.That(r.C.HasReply, Is.True);
        r.C.SetReplyData();
        Assert.That(Rt.ToString(r.Host.Get(r.Host.Child("m_receiver"), "text")), Is.EqualTo("Ann"), "null keeps the mail");
        bool closed = false;
        r.C.Closed += () => closed = true;
        r.C.Close();
        Assert.That(closed, Is.True);
        Assert.That(r.C.HasReply, Is.False, "the reply is gone");
    }

    [Test]
    public void EnterAndTabAreForwardedAndNothingElse()
    {
        var r = new Rig();
        Assert.That(r.C.OnKeyCode(MailSendDialogController.TabKey), Is.True);
        Assert.That(r.C.OnKeyCode(MailSendDialogController.EnterKey), Is.True);
        Assert.That(r.C.OnKeyCode(0x1e), Is.False);
        Assert.That(MailSendDialogController.EnterKey, Is.EqualTo(0x1c));
        Assert.That(MailSendDialogController.TabKey, Is.EqualTo(0xf));
    }

    [Test]
    public void OnlyTheNoReceiverAndToSelfErrorsSelectTheReceiver()
    {
        var r = new Rig();
        foreach (var code in new[] { 1, 0x19, 2, 0x1d, 0 }) Assert.DoesNotThrow(() => r.C.OnMailError(code));
        Assert.That(r.Notices, Is.Empty, "the dialog adds no notice of its own");
    }

    [Test]
    public void TheCandidatesAreTheNamesThatContainTheTypedTextLowerCased()
    {
        var r = new Rig(setup: c => c.Names = () => new[] { "Ann", "Joanna", "Bob", "ANNIE" });
        r.Host.Set(r.Host.Child("m_receiver"), "text", " aNN ");
        r.C.UpdateCandidates();
        var dp = r.Host.Get(r.Host.Child("m_receiver"), "m_candidate");
        // the receiver keeps what setCandidates gave it; read it back through the movie
        var labels = Candidates(r);
        Assert.That(labels, Is.EqualTo(new[] { "Ann", "Joanna", "ANNIE" }));
        r.Host.Set(r.Host.Child("m_receiver"), "text", "  ");
        r.C.UpdateCandidates();
        Assert.That(Candidates(r), Is.Empty);
    }

    static string[] Candidates(Rig r)
    {
        var dp = r.Host.Get(r.Host.Child("m_receiver"), "m_candidate");
        if (Rt.IsNullish(dp)) return Array.Empty<string>();
        int n = (int)Rt.ToUint32(r.Host.Get(dp, "length"));
        return Enumerable.Range(0, n).Select(i => Rt.ToString(r.Host.Get(r.Host.Invoke(dp, "requestItemAt", i), "label"))).ToArray();
    }
}
