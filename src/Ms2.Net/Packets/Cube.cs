using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>PC::SendRequestCube (@0x14165f170, pc.cpp:1929-1937): with an item held, the item is let go and RequestCube
/// (0x37) is sent with u8 0x12 and nothing else.</summary>
public sealed record HeldItemReleaseRequest : IOutPacket
{
    public Outbound Op => Outbound.RequestCube;
    public void Write(ByteWriter w) => w.WriteByte(0x12);
}

/// <summary>Unk_PCCubeCraft::ConfirmPlacement (@0x1410aad50, cubecraft_2.cpp:936-1063): RequestCube (0x37) with the u8
/// mode, the cell key, the craft's CubeItemInfo (FUN_1408b39c0) and the yaw as a float. Mode <see cref="PlaceMode"/> for a
/// free cell (:996-1002, the key FUN_1408a13d0), <see cref="ReplaceMode"/> for a cell holding another item (:1025-1030,
/// the key FUN_1408a03c0(FUN_1408a1430), the same key).</summary>
public sealed record CubePlaceRequest(int CellKey, CubeItemInfo Item, float Yaw, byte Mode = CubePlaceRequest.PlaceMode) : IOutPacket
{
    public const byte PlaceMode = 10;
    public const byte ReplaceMode = 0xf;
    public Outbound Op => Outbound.RequestCube;
    public void Write(ByteWriter w)
    {
        w.WriteByte(Mode);
        w.WriteInt(CellKey);
        Item.Write(w);
        w.WriteFloat(Yaw);
    }
}

/// <summary>
/// Inbound ResponseCube (RecvOp 0x69, commonfiles/opcodes.h:113), CSceneStateGame::OnResponseCube
/// (scenestategame.cpp:9876): a u8 mode. Read are the two held-item modes; other modes are read as null.
/// <list type="bullet">
/// <item>0x11 (:10145-10167): u8 error; error 0: i32 pc id, i32 cube key, i32 item id, i32 value (the cube's weapon value
/// m_n1c, then the pc holds the item); otherwise i32 the character the build notice is for.</item>
/// <item>0x12 (:10176-10192): u8 error; then i32 id, which with error 0 is the pc whose held item is let go
/// (PC::FUN_14165f1f0(0, 0)) and otherwise the character the build notice is for.</item>
/// </list>
/// </summary>
public sealed record ResponseCubeHeldItem(byte Mode, byte Error, int Id, int CubeKey = 0, int ItemId = 0, int Value = 0)
{
    public const byte ModeHold = 0x11;
    public const byte ModeRelease = 0x12;

    public static ResponseCubeHeldItem Read(ByteReader r)
    {
        byte mode = r.ReadByte();
        if (mode is not (ModeHold or ModeRelease)) return null;
        byte error = r.ReadByte();
        if (mode == ModeHold && error == 0)
        {
            int id = r.ReadInt(), cube = r.ReadInt(), item = r.ReadInt(), value = r.ReadInt();
            return new ResponseCubeHeldItem(mode, error, id, cube, item, value);
        }
        return new ResponseCubeHeldItem(mode, error, r.ReadInt());
    }
}

/// <summary>
/// ResponseCube 0x14 (CSceneStateGame::OnResponseCube, scenestategame.cpp:10199-10203), the home's map info:
/// UserUGCMapInfo::FUN_1408b4220 (@0x1408b4220, ugcmapoptions.cpp:291) reads i32 the owner's object id (+0x18), i32 the plot id
/// (+0x1c), i32 (+0x20), i32 (+0x24), the unicode string (+0x28), i64 (+0x30, 0 = no home), i64 (+0x38), u8 the result
/// (the client's nResult, which the taxi refresh reads) and u8 the open type (+0x40: 0 public, 1 password, 2 private).
/// GMS2 (every recorded 0x14) has one more i32 after +0x24 (0 in all of them) and one byte after +0x38, which this reads as
/// the result (1 as in the KMS2 recordings); it has no open type byte, so OpenType stays 0.
/// STILL-OPEN: what +0x20, +0x24, +0x28 and +0x38 mean is not recovered, so they keep the client's offsets as names.
/// </summary>
public sealed record ResponseCubeHomeInfo(int OwnerObjectId, int PlotId, int Field20, int Field24, string Field28,
                                          long HomeId, long Field38, byte Result, byte OpenType)
{
    public const byte Mode = 0x14;

    /// <summary>The body after the mode byte.</summary>
    public static ResponseCubeHomeInfo Read(ByteReader r) => Read(r, ProtocolProfile.Kms2525);

    /// <summary>The body after the mode byte, in the profile's layout.</summary>
    public static ResponseCubeHomeInfo Read(ByteReader r, ProtocolProfile profile)
    {
        int owner = r.ReadInt(), plot = r.ReadInt(), f20 = r.ReadInt(), f24 = r.ReadInt();
        if (profile.IsGms2) r.ReadInt();
        var m = new ResponseCubeHomeInfo(owner, plot, f20, f24, r.ReadUnicodeString(), r.ReadLong(), r.ReadLong(), r.ReadByte(),
                                         profile.IsGms2 ? (byte)0 : r.ReadByte());
        r.ExpectEnd("ResponseCube 0x14");
        return m;
    }
}

/// <summary>ResponseCube 0x27 (CSceneStateGame::OnResponseCube, scenestategame.cpp:10560-10589): i64 account id; for the
/// own account (DAT_143ab2820) i64 the day-change time (DAT_143ab2928), i64 the housing grade (kept as u16, DAT_143ab291c),
/// i64 the housing exp (DAT_143ab2930), i32 n and n i32 the grades whose reward is taken (DAT_143ab32b8). Another account's
/// packet stops after the id (<see cref="Mine"/> false), as the client returns there.</summary>
public sealed record ResponseCubeHousingGrade(bool Mine, long DayChangeTime, ushort Grade, long Exp, int[] RewardedGrades)
{
    public const byte Mode = 0x27;

    /// <summary>The body after the mode byte.</summary>
    public static ResponseCubeHousingGrade Read(ByteReader r, long ownAccount)
    {
        if (r.ReadLong() != ownAccount) return new ResponseCubeHousingGrade(false, 0, 0, 0, Array.Empty<int>());
        long time = r.ReadLong();
        ushort grade = (ushort)r.ReadLong();
        long exp = r.ReadLong();
        int n = r.ReadInt();
        var grades = new int[Math.Max(n, 0)];
        for (int i = 0; i < n; i++) grades[i] = r.ReadInt();
        r.ExpectEnd("ResponseCube 0x27");
        return new ResponseCubeHousingGrade(true, time, grade, exp, grades);
    }
}

/// <summary>ResponseCube 0x35 (scenestategame.cpp:10590-10606): i64 account id, i64 the house owner's id, i64, i64; for the
/// own account the pc is in a house (DAT_143ab2857) of that owner (DAT_143ab2938).
/// STILL-OPEN: the two last values go to the cube map service (CubeMapInformationService f420 / f428 through FUN_1410d5700 /
/// FUN_1410d5750) for the home info dialog's UpdateBalance; their meaning is not recovered and nothing ported reads them.</summary>
public sealed record ResponseCubeHouseOwner(long AccountId, long OwnerId, long Value1, long Value2)
{
    public const byte Mode = 0x35;

    public static ResponseCubeHouseOwner Read(ByteReader r)
    {
        var m = new ResponseCubeHouseOwner(r.ReadLong(), r.ReadLong(), r.ReadLong(), r.ReadLong());
        r.ExpectEnd("ResponseCube 0x35");
        return m;
    }
}

/// <summary>ResponseCube 0x37 (scenestategame.cpp:10746-10759): i16 the house owner's housing grade (DAT_143ab2940), i32 n,
/// n (i32 trophy id, i32 grade): the owner's trophy set (DAT_143ab32d8), a set, so a repeated pair counts once.</summary>
public sealed record ResponseCubeOwnerTrophies(ushort OwnerGrade, (int Id, int Level)[] Trophies)
{
    public const byte Mode = 0x37;

    public static ResponseCubeOwnerTrophies Read(ByteReader r)
    {
        ushort grade = (ushort)r.ReadShort();
        int n = r.ReadInt();
        var list = new List<(int, int)>();
        for (uint i = 0; i < (uint)n; i++) list.Add((r.ReadInt(), r.ReadInt()));
        r.ExpectEnd("ResponseCube 0x37");
        return new ResponseCubeOwnerTrophies(grade, list.ToArray());
    }
}

/// <summary>
/// ResponseCube 0xa and 0xf, the place answers (CSceneStateGame::OnResponseCube, scenestategame.cpp:9984-10034 and
/// :10102-10140): u8 error, i32, i32 character id, and with error 0 the cube.
/// <list type="bullet">
/// <item>0xa (:9984-10011): i32 (passed on unused), i32 character id, i32 group and i32 floor (Cube field50 / field54),
/// i32 the packed cell (FUN_1408a0400), i64 owner (Cube field18), the CubeItemInfo, u8 liftable (Cube field4c), f32
/// rotation, i32 house (Cube fieldD8), u8 notice flag; flag 1: the notice's text and u8 type (NoticeCubeInfo::Set).</item>
/// <item>0xf (:10102-10121): i32 (unused), i32 character id, i32 the packed cell, i64 owner, the CubeItemInfo, u8
/// liftable, f32 rotation, i32 house; group and floor are the replaced cube's.</item>
/// <item>An error (:10012-10016, :10123-10127): i32, i32 character id.</item>
/// </list>
/// </summary>
public sealed record ResponseCubePlace(byte Mode, byte Error, int CharacterId, int Cell = 0, long Owner = 0, CubeItemInfo Item = null,
    bool Liftable = false, float Rotation = 0, int House = 0, int Group = 0, int Floor = 0, string NoticeText = null, byte NoticeType = 0)
{
    public const byte ModePlace = 0xa;
    public const byte ModeReplace = 0xf;

    public static ResponseCubePlace Read(ByteReader r)
    {
        byte mode = r.ReadByte();
        if (mode is not (ModePlace or ModeReplace)) return null;
        byte error = r.ReadByte();
        r.ReadInt();
        int character = r.ReadInt();
        if (error != 0)
        {
            r.ExpectEnd("ResponseCube place");
            return new ResponseCubePlace(mode, error, character);
        }
        int group = 0, floor = 0;
        if (mode == ModePlace) { group = r.ReadInt(); floor = r.ReadInt(); }
        int cell = r.ReadInt();
        long owner = r.ReadLong();
        var item = CubeItemInfo.Read(r);
        bool liftable = r.ReadByte() != 0;
        float rotation = r.ReadFloat();
        int house = r.ReadInt();
        string text = null;
        byte type = 0;
        // :10003-10010: only a flag of 1 has a notice
        if (mode == ModePlace && r.ReadByte() == 1) { text = r.ReadUnicodeString(); type = r.ReadByte(); }
        r.ExpectEnd("ResponseCube place");
        return new ResponseCubePlace(mode, error, character, cell, owner, item, liftable, rotation, house, group, floor, text, type);
    }
}

/// <summary>
/// Inbound LoadCubes mode 0 (RecvOp 0x6A, commonfiles/opcodes.h:114), the house's cubes: CSceneStateGame::OnLoadCubes
/// (@0x141728110, scenestategame.cpp:10857-10897). u8 mode; mode 0: u8 the floor state (FUN_1410c3f50), i32 n, then n
/// cubes of i32 the packed cell (FUN_1408a0400), i64 owner, the CubeItemInfo (CubeItemInfo(CInPacket), ugcmapoptions.cpp:91),
/// i32 group and i32 floor (Cube field50 / field54), u8 liftable (field4c), f32 rotation, i32 house (fieldD8), u8 notice flag;
/// flag 1: the notice's text and u8 type (NoticeCubeInfo::Set). Null for another mode (1-3 are the plot states, owners and
/// sale states, <see cref="LoadCubesPlots"/>). The client reads the cubes only without the launch option IsSkipLoadingUgcCube, which ours
/// does not have.
/// </summary>
public sealed record LoadCubes(byte FloorState, IReadOnlyList<ResponseCubePlace> Cubes)
{
    public const byte ModeCubes = 0;

    public static LoadCubes Read(ByteReader r)
    {
        if (r.ReadByte() != ModeCubes) return null;
        byte state = r.ReadByte();
        int n = r.ReadInt();
        var cubes = new List<ResponseCubePlace>(Math.Max(0, Math.Min(n, 4096)));
        for (int i = 0; i < n; i++)
        {
            int cell = r.ReadInt();
            long owner = r.ReadLong();
            var item = CubeItemInfo.Read(r);
            int group = r.ReadInt();
            int floor = r.ReadInt();
            bool liftable = r.ReadByte() != 0;
            float rotation = r.ReadFloat();
            int house = r.ReadInt();
            string text = null;
            byte type = 0;
            // :10882-10889: only a flag of 1 has a notice
            if (r.ReadByte() == 1) { text = r.ReadUnicodeString(); type = r.ReadByte(); }
            cubes.Add(new ResponseCubePlace(ResponseCubePlace.ModePlace, 0, 0, cell, owner, item, liftable, rotation, house, group, floor, text, type));
        }
        r.ExpectEnd("LoadCubes");
        return new LoadCubes(state, cubes);
    }
}

/// <summary>CubeItemInfo (ugcmapoptions.cpp): a cube's item id, instance id, count and its optional UGC look.</summary>
public sealed class CubeItemInfo
{
    public int ItemId;
    public long InstanceId;
    public long Count;
    /// <summary>The uploaded look (CUgcItemLook), null without one.</summary>
    public UgcLook Look;

    /// <summary>CubeItemInfo::FUN_1408b3860 (@0x1408b3860, ugcmapoptions.cpp:132-137): an item id, a look and the look's
    /// uid (CUgcItemLook m_n18) all nonzero.</summary>
    public bool HasUploadedLook => ItemId != 0 && Look != null && Look.Uid != 0;

    /// <summary>CubeItemInfo::IsSameItem (@0x1408b38c0, ugcmapoptions.cpp:142-166): false for null; without an uploaded look
    /// the other must have none and the same item id; with one the other must have one too, the same item id and the same
    /// look uid.</summary>
    public bool IsSameItem(CubeItemInfo other)
    {
        if (other == null) return false;
        if (!HasUploadedLook)
        {
            if (other.HasUploadedLook) return false;
            return other.ItemId == ItemId;
        }
        if (!other.HasUploadedLook) return false;
        if (ItemId != other.ItemId) return false;
        if (Look == null) return false;
        return Look.Uid == other.Look.Uid;
    }

    /// <summary>CubeItemInfo::FUN_1408b3aa0 (@0x1408b3aa0, ugcmapoptions.cpp:181): i32 item id, i64 instance id, i64 count,
    /// u8 has look, then the look (CUgcItemLook::Read).</summary>
    public static CubeItemInfo Read(ByteReader r)
    {
        var info = new CubeItemInfo { ItemId = r.ReadInt(), InstanceId = r.ReadLong(), Count = r.ReadLong() };
        if (r.ReadByte() != 0) info.Look = ItemReader.ReadUgcLook(r);
        return info;
    }

    /// <summary>CubeItemInfo::FUN_1408b39c0 (@0x1408b39c0, ugcmapoptions.cpp:170): the same fields, the flag 1 when a look
    /// follows.</summary>
    public void Write(ByteWriter w)
    {
        w.WriteInt(ItemId);
        w.WriteLong(InstanceId);
        w.WriteLong(Count);
        w.WriteByte((byte)(Look != null ? 1 : 0));
        if (Look != null) ItemReader.WriteUgcLook(w, Look);
    }
}

/// <summary>
/// Inbound SetCraftMode (RecvOp 0x68, commonfiles/opcodes.h:112), CSceneStateGame::OnSetCraftMode (@0x141723410,
/// scenestategame.cpp:7827-7851): i32 pc id, u8 cube mode; a nonzero mode is followed by the CubeItemInfo and an i32, the
/// craft mode PC::vfunc_135 hands to the cube craft (FUN_141659250, pc.cpp:3040; Unk_PCCubeCraft's m_nMode, 2 = a held
/// lift, cubecraft_2.cpp:332).
/// </summary>
public sealed record SetCraftMode(int Id, byte CubeMode, CubeItemInfo Item, int CraftMode)
{
    public static SetCraftMode Read(ByteReader r)
    {
        int id = r.ReadInt();
        byte mode = r.ReadByte();
        if (mode == 0) { r.ExpectEnd("SetCraftMode"); return new SetCraftMode(id, 0, null, 0); }
        var item = CubeItemInfo.Read(r);
        int craft = r.ReadInt();
        r.ExpectEnd("SetCraftMode");
        return new SetCraftMode(id, mode, item, craft);
    }
}

/// <summary>
/// Inbound LoadCubes modes 1-3, CSceneStateGame::OnLoadCubes (@0x141728110, scenestategame.cpp:10902-10944): u8 mode,
/// i32 n, then n entries:
/// <list type="bullet">
/// <item>mode 1 (:10902-10911): i32 plot, u8 state (FUN_1410bbc00 takes the state and does not use it);</item>
/// <item>mode 2 (:10912-10928): i32 plot, i32 block, unicode the owner's name, i64 the owner's id (SetCubeOwner);</item>
/// <item>mode 3 (:10929-10941): i32 plot, i32 block, u8 state, i64 value (SetSaleState).</item>
/// </list>
/// Null for mode 0 (<see cref="LoadCubes"/>) and for a mode the client does not handle (the switch has no default).
/// </summary>
public sealed record LoadCubesPlots(byte Mode, IReadOnlyList<(int Plot, byte State)> States,
    IReadOnlyList<LoadCubesPlots.PlotOwner> Owners, IReadOnlyList<LoadCubesPlots.PlotSale> Sales)
{
    public const byte ModeStates = 1, ModeOwners = 2, ModeSales = 3;

    public sealed record PlotOwner(int Plot, int Block, string Name, long OwnerId);
    public sealed record PlotSale(int Plot, int Block, byte State, long Value);

    public static LoadCubesPlots Read(ByteReader r)
    {
        byte mode = r.ReadByte();
        if (mode < ModeStates || mode > ModeSales) return null;
        int n = r.ReadInt();
        int cap = Math.Max(0, Math.Min(n, 4096));
        var states = new List<(int, byte)>(mode == ModeStates ? cap : 0);
        var owners = new List<PlotOwner>(mode == ModeOwners ? cap : 0);
        var sales = new List<PlotSale>(mode == ModeSales ? cap : 0);
        for (int i = 0; i < n; i++)
        {
            int plot = r.ReadInt();
            if (mode == ModeStates) { states.Add((plot, r.ReadByte())); continue; }
            int block = r.ReadInt();
            if (mode == ModeOwners) { string name = r.ReadUnicodeString(); owners.Add(new PlotOwner(plot, block, name, r.ReadLong())); }
            else { byte state = r.ReadByte(); sales.Add(new PlotSale(plot, block, state, r.ReadLong())); }
        }
        r.ExpectEnd("LoadCubes");
        return new LoadCubesPlots(mode, states, owners, sales);
    }
}

/// <summary>ResponseCube 0x2a (scenestategame.cpp:10619-10625): u8 row, u8 on (1 is on, anything else off): the home option row's switch
/// (CubeMapInformationService FUN_1410c3e40 into the row map at +0x368). The packet is read whether or not the service exists.</summary>
public sealed record ResponseCubeRowSwitch(byte Row, bool On)
{
    public const byte Mode = 0x2a;

    public static ResponseCubeRowSwitch Read(ByteReader r)
    {
        var m = new ResponseCubeRowSwitch(r.ReadByte(), r.ReadByte() == 1);
        r.ExpectEnd("ResponseCube 0x2a");
        return m;
    }
}

/// <summary>ResponseCube 0x2b (scenestategame.cpp:10626-10633): u8 row, u8 level: the home option row's level (FUN_1410c3f10 into the map at +0x378).</summary>
public sealed record ResponseCubeRowLevel(byte Row, byte Level)
{
    public const byte Mode = 0x2b;

    public static ResponseCubeRowLevel Read(ByteReader r)
    {
        var m = new ResponseCubeRowLevel(r.ReadByte(), r.ReadByte());
        r.ExpectEnd("ResponseCube 0x2b");
        return m;
    }
}

/// <summary>
/// ResponseCube 0x24 (cube) and 0x43 (blueprint): the server's bill, UIUGCCubeBillDialog's FUN_140ee8190 (@0x140ee8190, uiugccubebilldialog.cpp:277-303)
/// and FUN_140ee83f0 (@0x140ee83f0, :330-358; scenestategame.cpp:10405-10409): u8 count, i32 the description's argument, count times (u8 pay type, i64 amount);
/// the blueprint's then has bool preview. The amounts keep the client's order: a map by pay type, a later row of the same type replaces the earlier.
/// </summary>
public sealed record ResponseCubeBill(byte Mode, int Arg, IReadOnlyList<KeyValuePair<int, long>> Amounts, bool Preview)
{
    public const byte ModeCube = 0x24;
    public const byte ModeBlueprint = 0x43;

    public static ResponseCubeBill Read(ByteReader r, byte mode)
    {
        int count = r.ReadByte();
        int arg = r.ReadInt();
        var map = new SortedDictionary<int, long>();
        for (int i = 0; i < count; i++) { int type = r.ReadByte(); map[type] = r.ReadLong(); }
        bool preview = mode == ModeBlueprint && r.ReadBool();
        // no ExpectEnd: the client's reader of these modes never checks the end, trailing bytes are ignored
        return new ResponseCubeBill(mode, arg, map.ToArray(), preview) { Count = count };
    }

    /// <summary>The row count the packet carried (nCount; the map can hold fewer when a pay type repeats).</summary>
    public int Count { get; init; }
}
