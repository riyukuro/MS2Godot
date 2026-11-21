using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The party search packets (CMatchPartyRecruit, CPartyManager::OnMatchParty and SendMatchPartyMode*) and the party
/// book's listing state.</summary>
public class PartySearchPacketTests {
    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
    static byte[] Body(Action<ByteWriter> write) { var w = new ByteWriter(); write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
    static byte[] Concat(params byte[][] parts) => parts.SelectMany(b => b).ToArray();
    static byte[] Str(string s) => Concat(BitConverter.GetBytes((ushort) s.Length), System.Text.Encoding.Unicode.GetBytes(s));

    internal static PartyRecruit Recruit(long id, string title = "go", string leader = "Lead", long leaderChar = 11) => new() {
        Id = id, PartyId = 9, Category = 3, Kind = 4, Title = title, AutoJoin = true, MemberCount = 2, MaxMembers = 4,
        LeaderAccountId = 111, LeaderCharacterId = leaderChar, LeaderName = leader, Created = 1700000000,
    };

    /// <summary>The listing's bytes in the order of CMatchPartyRecruit::Read (FUN_140694dc0).</summary>
    static void WriteRecruitByHand(ByteWriter w, long id) {
        w.WriteLong(id); w.WriteInt(9); w.WriteInt(3); w.WriteInt(4); w.WriteUnicodeString("go");
        w.WriteByte(1); w.WriteInt(2); w.WriteInt(4);
        w.WriteLong(111); w.WriteLong(11); w.WriteUnicodeString("Lead"); w.WriteLong(1700000000);
    }

    static PartySearchMessage ReadSearch(byte[] body) {
        var r = new ByteReader(body);
        var m = PartySearchMessage.Read(r);
        Assert.That(r.Available, Is.Zero, "the whole body is read");
        return m;
    }

    static PartyMessage ReadParty(byte[] body) {
        var r = new ByteReader(body);
        var m = PartyMessage.Read(r);
        Assert.That(r.Available, Is.Zero, "the whole body is read");
        return m;
    }

    [Test]
    public void OpcodesAreTheClientOps() {
        Assert.That((ushort) Inbound.PartySearch, Is.EqualTo(0xBB));     // RecvOp PartySearch (opcodes.h:194)
        Assert.That((ushort) Outbound.PartySearch, Is.EqualTo(0x6E));    // SendOp PartySearch (opcodes.h:415)
        Assert.That(new PartyRecruitRemoveRequest().Op, Is.EqualTo(Outbound.PartySearch));
        Assert.That(new PartyRecruitJoinRequest(0, "", 0).Op, Is.EqualTo(Outbound.Party));
    }

    [Test]
    public void TheListingReadsInTheClientOrderAndWritesBack() {
        var bytes = Body(w => WriteRecruitByHand(w, 77));
        var r = PartyRecruit.Read(new ByteReader(bytes));
        Assert.That((r.Id, r.PartyId, r.Category, r.Kind, r.Title, r.AutoJoin), Is.EqualTo((77L, 9, 3, 4, "go", true)));
        Assert.That((r.MemberCount, r.MaxMembers, r.LeaderAccountId, r.LeaderCharacterId, r.LeaderName, r.Created), Is.EqualTo((2, 4, 111L, 11L, "Lead", 1700000000L)));
        Assert.That(Body(r.Write), Is.EqualTo(bytes));
        Assert.That(new PartyRecruit().IsEmpty, Is.True);
    }

    [Test]
    public void TheRequestsWriteTheClientBytes() {
        Assert.That(Bytes(new PartyRecruitRegisterRequest("ab", true, 6)), Is.EqualTo(Concat(new byte[] { 0 }, Str("ab"), new byte[] { 1 }, BitConverter.GetBytes(6))));
        Assert.That(Bytes(new PartyRecruitRemoveRequest()), Is.EqualTo(new byte[] { 1 }));
        var cond = new PartySearchCondition { Text = "x", Page = 3 };
        Assert.That(Bytes(cond.ToRequest()), Is.EqualTo(Concat(new byte[] { 2 }, BitConverter.GetBytes(0), BitConverter.GetBytes(0), new byte[] { 0xc }, Str("x"),
            BitConverter.GetBytes(3), BitConverter.GetBytes(0))));
        Assert.That(Bytes(new PartyRecruitJoinRequest(9, "Lead", 77)), Is.EqualTo(Concat(new byte[] { 0x17 }, BitConverter.GetBytes(9), Str("Lead"), BitConverter.GetBytes(77L))));
    }

    [Test]
    public void TheModesRead() {
        var reg = (PartyRecruitRegistered) ReadSearch(Body(w => { w.WriteByte(0); WriteRecruitByHand(w, 5); }));
        Assert.That(reg.Recruit.Id, Is.EqualTo(5));
        Assert.That(((PartyRecruitRemoved) ReadSearch(Body(w => { w.WriteByte(1); w.WriteLong(5); }))).Id, Is.EqualTo(5));
        var res = (PartySearchResults) ReadSearch(Body(w => { w.WriteByte(2); w.WriteInt(3); w.WriteByte(1); WriteRecruitByHand(w, 1); w.WriteByte(0); w.WriteByte(1); WriteRecruitByHand(w, 2); }));
        Assert.That(res.Recruits.Select(x => x.Id), Is.EqualTo(new[] { 1L, 2L }));   // the entry flagged 0 has no body
        Assert.That(ReadSearch(Body(w => { w.WriteByte(4); w.WriteByte(1); w.WriteInt(0x67); })), Is.EqualTo(new PartySearchError(1, 0x67)));
    }

    [Test]
    public void ThePartyInfoKeepsItsListing() {
        var info = (PartyInfo) ReadParty(new PartyInfo(false, 9, 11, Array.Empty<PartyInfoMember>(), false, 0) { Recruit = Recruit(77) }.Write());
        Assert.That(info.Recruit?.Id, Is.EqualTo(77));
        var book = new PartyBook();
        var shown = new List<long>();
        book.RecruitShown += (r, _) => shown.Add(r.Id);
        book.Apply(info, 11, false);
        Assert.That(book.Recruitment.Id, Is.EqualTo(77));
        Assert.That(shown, Is.EqualTo(new[] { 77L }));
    }

    [Test]
    public void ModeOneAReadsTheListingAndChangesTheOwn() {
        var up = (PartyRecruitUpdate) ReadParty(Body(w => { w.WriteByte(0x1a); w.WriteByte(1); WriteRecruitByHand(w, 77); }));
        Assert.That(up.Recruit.Id, Is.EqualTo(77));
        var book = new PartyBook();
        var notices = new List<int>();
        book.Notice += (id, _) => notices.Add(id);
        book.Apply(up, 11, false);
        Assert.That(book.Recruitment.Id, Is.EqualTo(77));
        book.Apply(ReadParty(new byte[] { 0x1a, 0 }), 11, false);           // no listing: the own one goes
        Assert.That(book.Recruitment.IsEmpty, Is.True);
        Assert.That(notices, Is.EqualTo(new[] { 0xb2e, 0xb30 }));
    }

    [Test]
    public void ResultsOfAFirstPageReplaceTheListAndLaterPagesAppend() {
        var book = new PartyBook();
        var sent = new List<IOutPacket>();
        book.Send = sent.Add;
        var got = new List<(int count, int last)>();
        book.ResultsReceived += (l, last) => got.Add((l.Count, last));
        book.Search(new PartySearchCondition());
        Assert.That(sent.Single(), Is.EqualTo(new PartySearchRequest(0, 0, 0xc, "", 1, 0)));
        book.Apply(new PartySearchResults(new[] { Recruit(1), Recruit(2) }));
        book.Search(new PartySearchCondition { Page = 2 });                       // not a first page: appended
        book.Apply(new PartySearchResults(new[] { Recruit(3) }));
        Assert.That(got, Is.EqualTo(new[] { (2, 0), (3, 1) }));
        Assert.That(book.FindRecruit(3)?.Id, Is.EqualTo(3));
        book.Search(new PartySearchCondition());
        book.Apply(new PartySearchResults(new[] { Recruit(4) }));
        Assert.That(book.Results.Select(r => r.Id), Is.EqualTo(new[] { 4L }));
        Assert.That(book.FindRecruit(1), Is.Null);
    }

    [Test]
    public void RegisterRemoveAndJoinSendAndTheAnswersKeepTheListing() {
        var book = new PartyBook();
        var sent = new List<IOutPacket>(); var notices = new List<int>();
        book.Send = sent.Add;
        book.Notice += (id, _) => notices.Add(id);
        book.RemoveRecruit();                                                    // no own listing: nothing
        book.RegisterRecruit("ab", false, 4);
        Assert.That(book.RecruitPending, Is.True);
        book.Apply(new PartyRecruitRegistered(Recruit(77)));
        Assert.That((book.RecruitPending, book.Recruitment.Id), Is.EqualTo((false, 77L)));
        book.RemoveRecruit();
        book.Apply(new PartyRecruitRemoved(78));                                 // another id: kept
        Assert.That(book.Recruitment.Id, Is.EqualTo(77));
        book.Apply(new PartyRecruitRemoved(77));
        Assert.That(book.Recruitment.IsEmpty, Is.True);
        book.JoinRecruit(Recruit(5, leader: "Me"), "me");                        // the own name: notice 0x32e
        book.JoinRecruit(Recruit(5, leader: ""), "me");                          // no name: nothing
        book.JoinRecruit(Recruit(5), "me");
        Assert.That(sent, Is.EqualTo(new IOutPacket[] { new PartyRecruitRegisterRequest("ab", false, 4), new PartyRecruitRemoveRequest(), new PartyRecruitJoinRequest(9, "Lead", 5) }));
        Assert.That(notices, Is.EqualTo(new[] { 0xb2e, 0xb30, 0x32e }));
    }

    [Test]
    public void TheErrorsMapToTheClientMessages() {
        var book = new PartyBook();
        var boxes = new List<int>(); var keys = new List<string>(); var notices = new List<(int, object[])>(); int titleCleared = 0, textCleared = 0;
        book.Box += boxes.Add; book.BoxByKey += keys.Add; book.Notice += (id, a) => notices.Add((id, a));
        book.RegisterTitleCleared += () => titleCleared++; book.SearchTextCleared += () => textCleared++;
        book.RegisterRecruit("ab", false, 4);
        book.Apply(new PartySearchError(0, 0x6c));
        Assert.That((book.RecruitPending, titleCleared, boxes.Last()), Is.EqualTo((false, 1, 0xb4e)));
        book.Apply(new PartySearchError(2, 0x6c));
        Assert.That((textCleared, boxes.Last()), Is.EqualTo((1, 0xb4f)));
        book.Apply(new PartySearchError(1, 0xc));
        Assert.That(boxes.Last(), Is.EqualTo(0xb4a));
        book.Apply(new PartyRecruitRegistered(Recruit(77)));
        book.Apply(new PartySearchError(1, 0x6a));                              // the listing is gone on the server
        Assert.That((keys.Single(), book.Recruitment.IsEmpty), Is.EqualTo(("s_partysearch_err_server_not_find_recruit", true)));
        book.Apply(new PartySearchError(1, 0x69));                              // 0x69 is a register error only: the formatted fallback
        Assert.That(notices.Last().Item1, Is.EqualTo(0xb52));
        Assert.That(notices.Last().Item2, Is.EqualTo(new object[] { 1, 0x69 }));
    }

    [Test]
    public void ADisbandClearsTheListing() {
        var book = new PartyBook();
        book.Apply(new PartyRecruitUpdate(Recruit(77)), 11, false);
        book.Apply(new PartyDisband(), 11, false);
        Assert.That(book.Recruitment.IsEmpty, Is.True);
    }
}
