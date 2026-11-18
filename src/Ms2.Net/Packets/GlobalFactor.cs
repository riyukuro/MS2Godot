// Layout from the restored client's CSceneStateGame::OnGlobalFactor (scenestategame.cpp:4851, 0x1417380c0) and CGloabalFactor::ReadRates (gloabalfactor.cpp, 0x1404c18c0).
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>
/// The client's CGloabalFactor rate tables: experience rates [11 kinds][level] and meso rates [3 kinds][level], all 1.0 until the server sends them.
/// Inbound 0xD6 carries 3 experience tables then 3 meso tables, each a u8 kind and 99 floats for levels 1..99. The client takes the packet once
/// (DAT_143ab2918) and then rescales every quest's completion exp and meso with <see cref="QuestRates"/> (QuestDataTable::FUN_14074ee30).
/// </summary>
public sealed class GlobalFactor {
    public const int ExpKinds = 11, MesoKinds = 3, Levels = 99;
    readonly float[][] exp = NewTable(ExpKinds), meso = NewTable(MesoKinds);
    /// <summary>True once a packet was applied; later packets are ignored like the client does.</summary>
    public bool Applied { get; private set; }
    /// <summary>The highest level the rate getters answer for (CFeatureManager::GetLevelCap); 1.0 above it.</summary>
    public int LevelCap { get; set; } = Levels;

    static float[][] NewTable(int kinds) { var t = new float[kinds][]; for (int k = 0; k < kinds; k++) { t[k] = new float[Levels + 1]; Array.Fill(t[k], 1f); } return t; }

    /// <summary>OnGlobalFactor: reads the six tables; false (and nothing changed) when a packet was already applied.</summary>
    public bool Apply(ByteReader r) {
        if (Applied) return false;
        // the client indexes by the kind byte unchecked; a kind outside its table is read and dropped here instead of writing out of bounds
        for (int t = 0; t < 6; t++) {
            int kind = r.ReadByte();
            var table = t < 3 ? exp : meso;
            bool ok = kind < table.Length;
            for (int lv = 1; lv <= Levels; lv++) { float f = r.ReadFloat(); if (ok) table[kind][lv] = f; }
        }
        Applied = true;
        return true;
    }

    public float ExpRate(int level, int kind) => level < 1 || level > LevelCap || kind < 0 || kind >= ExpKinds ? 1f : exp[kind][level];
    public float MesoRate(int level, int kind) => level < 1 || level > LevelCap || kind < 0 || kind >= MesoKinds ? 1f : meso[kind][level];

    /// <summary>The (exp, meso) factors QuestDataTable::FUN_14074ee30 applies to a quest of basic questType <paramref name="questType"/> and level: types 0, 1, 3 use exp kinds 5, 6, 7 and meso kinds 0, 1, 2; other types 1.0.</summary>
    public (float Exp, float Meso) QuestRates(int questType, int level) => questType switch {
        0 => (ExpRate(level, 5), MesoRate(level, 0)),
        1 => (ExpRate(level, 6), MesoRate(level, 1)),
        3 => (ExpRate(level, 7), MesoRate(level, 2)),
        _ => (1f, 1f),
    };
}
