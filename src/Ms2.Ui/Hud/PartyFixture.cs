using Ms2.Net.Packets;

namespace Ms2.Ui.Hud;

/// <summary>
/// FIXTURE party for the party window without a server: a party packet mode 9 (OnPartyInfo) written in the wire shape and read
/// back through <see cref="PartyMessage.Read"/>, with real job ids and map ids, not a recorded party. The own member is the HUD
/// fixture's level 21 Knight (character 2) and leads; one member is in the same map, one in another map and channel, one is
/// offline.
/// </summary>
public static class PartyFixture
{
    public const long SelfCharacterId = 2;

    public static byte[] InfoPacket(int mapId, PlayerState self)
    {
        PartyUser User(long id, string name, short level, int job, int hp, int maxHp, int map, ushort channel) => new()
        {
            AccountId = id + 100, CharacterId = id, Name = name, Level = level, Job = job, Hp = hp, MaxHp = maxHp, MapId = map, Channel = channel,
        };
        var info = new PartyInfo(false, 1, SelfCharacterId, new[]
        {
            new PartyInfoMember(false, User(SelfCharacterId, self.Name, (short)self.Level, self.SubJob, (int)self.HpCur, (int)self.HpMax, mapId, (ushort)self.Channel)),
            new PartyInfoMember(false, User(3, "FixtureArcher", 23, 500, 900, 1800, mapId, (ushort)self.Channel)),     // 500 Archer
            new PartyInfoMember(false, User(4, "FixturePriest", 20, 400, 1500, 1500, 2000001, 1)),                       // 400 Priest; 2000001 Tria
            new PartyInfoMember(true, User(5, "FixtureWizard", 18, 200, 1200, 1200, mapId, (ushort)self.Channel)),      // 200 Wizard, offline
        }, false, 0);
        return info.Write();
    }

    /// <summary>The fixture party, read from <see cref="InfoPacket"/>.</summary>
    public static PartyBook Book(int mapId, PlayerState self)
    {
        var book = new PartyBook();
        var r = new Maple2.PacketLib.Tools.ByteReader(InfoPacket(mapId, self));
        book.Apply(PartyMessage.Read(r), SelfCharacterId, false);
        return book;
    }
}
