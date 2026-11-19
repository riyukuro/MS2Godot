using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;

namespace Ms2.Ui.Hud;

/// <summary>
/// FIXTURE group chats for the group chat windows without a server: group chat packets (mode 0 the chat, mode 10 its lines)
/// written in the wire shape and read back through <see cref="GroupChatMessage.Read"/>, with real job ids, not a recorded
/// chat. The own member is the HUD fixture's character (<see cref="PartyFixture.SelfCharacterId"/>); chat 1 has two other
/// members (one invited and not joined) and three lines, chat 2 one other member and one line.
/// </summary>
public static class GroupChatFixture
{
    static PartyUser User(long id, string name, short level, int job) => new()
    {
        AccountId = id + 100, CharacterId = id, Name = name, Level = level, Job = job,
    };

    static byte[] AddPacket(int oid, params (int State, PartyUser User)[] members)
    {
        var w = new ByteWriter();
        w.WriteByte(0);
        var chat = new GroupChat { Oid = oid };
        foreach (var (state, user) in members) chat.AddMember(new GroupChatMember { State = state, User = user });
        chat.Write(w);
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }

    static byte[] TalkPacket(int oid, string name, string text)
    {
        var w = new ByteWriter();
        w.WriteByte(10);
        w.WriteInt(oid);
        w.WriteUnicodeString(name);
        w.WriteUnicodeString(text);
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }

    /// <summary>The fixture's packets in their order: the two chats, then their lines.</summary>
    public static IEnumerable<byte[]> Packets(PlayerState self)
    {
        var me = User(PartyFixture.SelfCharacterId, self.Name, (short)self.Level, self.SubJob);
        yield return AddPacket(1, (1, me), (1, User(3, "FixtureArcher", 23, 500)), (2, User(4, "FixturePriest", 20, 400)));   // 500 Archer, 400 Priest
        yield return AddPacket(2, (1, me), (1, User(5, "FixtureWizard", 18, 200)));                                           // 200 Wizard
        yield return TalkPacket(1, "FixtureArcher", "1");
        yield return TalkPacket(1, self.Name, "2");
        yield return TalkPacket(1, "FixtureArcher", "3");
        yield return TalkPacket(2, "FixtureWizard", "4");
    }

    /// <summary>The fixture's packets applied to <paramref name="book"/>.</summary>
    public static void Apply(GroupChatBook book, PlayerState self)
    {
        foreach (var p in Packets(self)) book.Apply(GroupChatMessage.Read(new ByteReader(p)));
    }
}
