using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>
/// GameObjectEffectNifCtx (gameobjecteffectnifctx.cpp, Read @0x140474870, :68): the nif a trigger hangs on a game object's
/// "Eff_UI" node: u32 game object id (m_nGameOID), the nif path under Model/ (m_strNifPath, a narrow string), bool outline
/// (m_bOutline), float scale (m_fScale) and float z rotation in degrees (m_fRotateZ).
/// </summary>
public sealed record TriggerEffectNif(int ObjectId, string NifPath, bool Outline, float Scale, float RotateZ)
{
    public static TriggerEffectNif Read(ByteReader r) => new(r.ReadInt(), r.ReadString(), r.ReadBool(), r.ReadFloat(), r.ReadFloat());
}

public static partial class TriggerObjects
{
    /// <summary>
    /// Trigger type 0x10 (WorldService::FUN_141bd42f0 @0x141bd42f0, worldservice_1.cpp:7302): the effect table read into
    /// WorldService m_848 by its operator&gt;&gt; (worldservice_2.cpp:225): u32 count, then per entry a u32 key and a
    /// <see cref="TriggerEffectNif"/>. The count is unsigned and the client reads that many; here it is capped by what the body
    /// can hold (an entry is at least 19 bytes), so a bad count reads what is there. The client reads no further, so the body
    /// is not checked for its end.
    /// </summary>
    public static List<(int Key, TriggerEffectNif Effect)> ReadLoadEffects(ByteReader r)
    {
        const int minEntry = 4 + 4 + 2 + 1 + 4 + 4;
        uint n = (uint)r.ReadInt();
        uint fits = (uint)(r.Available / minEntry);
        var list = new List<(int, TriggerEffectNif)>((int)Math.Min(n, fits));
        for (uint i = 0; i < n && r.Available >= minEntry; i++)
        {
            int key = r.ReadInt();
            list.Add((key, TriggerEffectNif.Read(r)));
        }
        return list;
    }

    /// <summary>Trigger type 0x12 (WorldService::RemoveEffect @0x141bd4670, worldservice_1.cpp:7337): the u32 game object id.</summary>
    public static int ReadRemoveEffect(ByteReader r) => r.ReadInt();
}
