using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>
/// The kind of a field trigger object, as TriggerObject::Create (@0x140de6260, triggeractioncli.cpp:450) picks it from the
/// entity's flat model: MS2TriggerSound (0x2147ae01), MS2TriggerMesh (0x74b363a7), MS2TriggerActor (0x4714f9ea),
/// MS2TriggerRope (0x8a9638bf) or MS2TriggerLadder (0xc680f858), MS2TriggerEffect (0x670a04d7), MS2TriggerCube (0x79197a62),
/// MS2TriggerCamera (0x65338ec3), in that order; any other one is a plain TriggerObject. Only an entity with the
/// MS2TriggerObject mixin (0x0e8dd99d) is made a trigger object at all (CTriggerService::FUN_141861620, triggerservice.cpp:324).
/// </summary>
public enum TriggerObjectKind { Plain, Sound, Mesh, Actor, Rope, Effect, Cube, Camera }

/// <summary>One object's event: its id and what the object's reader took (the fields its kind does not read stay default).</summary>
public sealed record TriggerObjectEvent(int Id, TriggerObjectKind Kind, bool Known)
{
    /// <summary>Mesh: show (vfunc_0); Actor / Rope / Effect / Cube: on (vfunc_0).</summary>
    public bool Show { get; init; }
    /// <summary>Mesh: the flag FUN_141545250 gets; Sound / Camera: the on byte; Effect: its second value (vfunc_1).</summary>
    public byte Flag { get; init; }
    /// <summary>Rope: its second bool (vfunc_0 again).</summary>
    public bool Flag2 { get; init; }
    /// <summary>Mesh / Rope: the fade in tenths of a second; Effect: its third value (vfunc_3).</summary>
    public int Fade { get; init; }
    /// <summary>Mesh: the model name a shown mesh swaps to when not empty; Actor: the animation (vfunc_6).</summary>
    public string Text { get; init; } = "";
    /// <summary>Mesh: the skin scale set with the swapped model (vfunc_5).</summary>
    public float Scale { get; init; }
    /// <summary>The bSkipFx the object's vfunc_2 gets: set for a type 2 event (ReadTriggerEvents, triggerservice.cpp:429),
    /// clear for type 3 (:435).</summary>
    public bool SkipFx { get; init; }
}

/// <summary>
/// The Trigger packet (RecvOp 0x4E, commonfiles/opcodes.h:86; CSceneStateGame::OnTrigger @0x141721270 hands it to
/// CTriggerService::FUN_141861840, triggerservice.cpp:366): u8 type, then for type 2 (ReadTriggerEvents, the field entry's
/// states, applied without effects) i32 count and count object events, for type 3 (FUN_141861ad0) one object event applied
/// with its effects. An object event (FUN_141863a90, triggerservice.cpp:951) is i32 id, then the fields the object's
/// vfunc_2 reads (triggeractioncli.cpp): Mesh (:643) bool show, u8 flag, i32 fade, string model, float scale; Actor (:716)
/// bool on, string animation; Rope (:738) bool, bool, i32 fade; Effect (:785) bool, u8, i32; Cube (:818) bool; Sound (:555)
/// and Camera (:848) u8; a plain TriggerObject (:435) nothing. An id the service does not hold reads nothing more and is
/// logged ("Trigger Object Sync Error"), so the client reads the next event from there; this does the same.
/// Type 5 (the camera objects to run) is read by <see cref="ReadCameraObjects"/>; the other types (popups, effects, ...) are not read here.
/// The argument block at +8 of CInPacket (Unk_TrigArg, triggeractioncli.h:150) is CInputStream (commonfiles/stream.h:25):
/// slot 0 ReadBool (bool), slot 1 ReadUInt8 (u8), slot 3 ReadInt32 (i32), slot 5 ReadFloat (float), and slot 6
/// ReadUnicodeStringA (commonfiles/packet.cpp:537, which reads the UTF-16 string via ReadUnicodeString).
/// </summary>
public static partial class TriggerObjects
{
    public const byte Load = 2, Update = 3, CameraObjects = 5, Guide = 9;

    /// <summary>
    /// The client's type (CTriggerService::FUN_141861840's switch, triggerservice.cpp:366) of a Trigger packet's type
    /// byte. KMS2 sends the client's numbers. GMS2 sends types 2, 3 and 5 unchanged and every type from KMS2 9 upward one
    /// lower (the upstream Maple2 server's Command: Ui 8, Dialog 14, SidePopup 18, SetVisibleUi 19; the recorded GMS2
    /// bodies of 8, 0xe, 0x12, 0x13 and 0x14 have the layouts of KMS2 9, 0xf, 0x13, 0x14 and 0x15), so a GMS2 type
    /// of 8 or more is one more here.
    /// </summary>
    public static byte ClientType(byte wire, ProtocolProfile profile) =>
        profile != null && profile.IsGms2 && wire >= 8 ? (byte)(wire + 1) : wire;

    /// <summary>
    /// The object events of a Trigger packet of type 2 or 3 (an empty list for another type). <paramref name="kindOf"/>
    /// gives the kind of a trigger object the field holds, or null for an id it does not.
    /// </summary>
    public static List<TriggerObjectEvent> Read(ByteReader r, Func<int, TriggerObjectKind?> kindOf) => Read(r, kindOf, out _);

    /// <summary>As <see cref="Read(ByteReader, Func{int, TriggerObjectKind?})"/>, giving the packet's type; the body of
    /// another type is left unread.</summary>
    public static List<TriggerObjectEvent> Read(ByteReader r, Func<int, TriggerObjectKind?> kindOf, out byte type)
    {
        var list = new List<TriggerObjectEvent>();
        type = r.ReadByte();
        if (type == Load)
        {
            int n = r.ReadInt();
            for (int i = 0; i < n; i++) list.Add(ReadEvent(r, kindOf) with { SkipFx = true });
        }
        else if (type == Update) list.Add(ReadEvent(r, kindOf));
        return list;
    }

    /// <summary>
    /// The body of a type 5 Trigger packet (CTriggerService::OnCameraObjects @0x141861b00, triggerservice.cpp:439-448):
    /// u8 count, count i32 camera object ids, u8 end.
    /// </summary>
    public static (List<int> Ids, bool End) ReadCameraObjects(ByteReader r)
    {
        int n = r.ReadByte();
        var ids = new List<int>(n);
        for (int i = 0; i < n; i++) ids.Add(r.ReadInt());
        return (ids, r.ReadByte() != 0);
    }

    static TriggerObjectEvent ReadEvent(ByteReader r, Func<int, TriggerObjectKind?> kindOf)
    {
        int id = r.ReadInt();
        if (kindOf(id) is not { } kind) return new TriggerObjectEvent(id, TriggerObjectKind.Plain, false);
        var e = new TriggerObjectEvent(id, kind, true);
        switch (kind)
        {
            case TriggerObjectKind.Mesh:
                return e with { Show = r.ReadBool(), Flag = r.ReadByte(), Fade = r.ReadInt(), Text = r.ReadUnicodeString(), Scale = r.ReadFloat() };
            case TriggerObjectKind.Actor:
                return e with { Show = r.ReadBool(), Text = r.ReadUnicodeString() };
            case TriggerObjectKind.Rope:
                return e with { Show = r.ReadBool(), Flag2 = r.ReadBool(), Fade = r.ReadInt() };
            case TriggerObjectKind.Effect:
                return e with { Show = r.ReadBool(), Flag = r.ReadByte(), Fade = r.ReadInt() };
            case TriggerObjectKind.Cube:
                return e with { Show = r.ReadBool() };
            case TriggerObjectKind.Sound:
            case TriggerObjectKind.Camera:
                return e with { Flag = r.ReadByte() };
            default:
                return e;
        }
    }

    /// <summary>
    /// The kind TriggerObject::Create gives an entity, <paramref name="hasMixin"/> answering FlatModel::ContainsModel by
    /// model name (the entity's own model or a mixin); null when the entity is no trigger object
    /// (it lacks the MS2TriggerObject mixin).
    /// </summary>
    public static TriggerObjectKind? KindOf(Func<string, bool> hasMixin)
    {
        if (!hasMixin("MS2TriggerObject")) return null;
        if (hasMixin("MS2TriggerSound")) return TriggerObjectKind.Sound;
        if (hasMixin("MS2TriggerMesh")) return TriggerObjectKind.Mesh;
        if (hasMixin("MS2TriggerActor")) return TriggerObjectKind.Actor;
        if (hasMixin("MS2TriggerRope") || hasMixin("MS2TriggerLadder")) return TriggerObjectKind.Rope;
        if (hasMixin("MS2TriggerEffect")) return TriggerObjectKind.Effect;
        if (hasMixin("MS2TriggerCube")) return TriggerObjectKind.Cube;
        if (hasMixin("MS2TriggerCamera")) return TriggerObjectKind.Camera;
        return TriggerObjectKind.Plain;
    }
}
