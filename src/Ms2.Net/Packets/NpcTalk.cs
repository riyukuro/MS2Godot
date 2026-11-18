// Layouts from the restored client's UITalkManager::OnTalkPacket (uitalkcinematic.cpp:971) and its mode 3 readers, and the client's
// NpcTalk / Quest writers (uitalkcinematic.cpp, cinematiccomponent.cpp). Checked against the Maple2 server fork's NpcTalkPacket
// writers and its NpcTalkHandler / QuestHandler readers (AGPL-3.0).
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// A decoded Inbound 0x4B packet, one record per OnTalkPacket mode. In modes 1, 2, 8 and 9 the client keeps the talk type
/// (m_nFlags), the script id (m_nParam), the page (m_nPage) and the button set (m_nSound), then looks up the script itself
/// (UITalkManager::FindTalkScript, uitalkcinematic.cpp:1945): type 8 the npc's select talk, 4 the quest script of the first quest id,
/// 2 or 0x20 the npc's script with that id, 1 the npc's job talk.
/// </summary>
public abstract record NpcTalkMessage {
    /// <summary>Mode 0: the talk is over (m_nTalking = 0, ResetTalk).</summary>
    public sealed record Close : NpcTalkMessage;
    /// <summary>Mode 1: the talk opens (FUN_141b1ff80): i32 npc object id, u8 type, i32 script id, i32 page, i32 button set.</summary>
    public sealed record Open(int ObjectId, int Flags, int ScriptId, int Page, int ButtonSet) : NpcTalkMessage;
    /// <summary>Mode 2: the next page (FUN_141b204d0): u8 type, i32 id (the quest list becomes this one id), i32 script id, i32 page,
    /// i32 button set.</summary>
    public sealed record Next(int Flags, int QuestId, int ScriptId, int Page, int ButtonSet) : NpcTalkMessage;
    /// <summary>Mode 8: like mode 2, then ReloadScript (the talk window is opened again).</summary>
    public sealed record Reload(int Flags, int QuestId, int ScriptId, int Page, int ButtonSet) : NpcTalkMessage;
    /// <summary>Mode 9: u8 type, i32 script id, i32 page, i32 button set; the quest list is cleared, then ReloadScript.</summary>
    public sealed record ReloadNoQuest(int Flags, int ScriptId, int Page, int ButtonSet) : NpcTalkMessage;
    /// <summary>Mode 3 sub 1 (SetNpcNameTag): the talk npc's name tag text.</summary>
    public sealed record NameTag(string Text) : NpcTalkMessage;
    /// <summary>Mode 3 sub 3 (FUN_141b1e8a0): i32, sent to the talk scene as "moveMyPC" (the Maple2 server writes a portal id).</summary>
    public sealed record MovePlayer(int Value) : NpcTalkMessage;
    /// <summary>Mode 3 sub 4 (FUN_141b1ea90): a dialog name and its argument (FUN_141b1eb90 opens it).</summary>
    public sealed record OpenDialog(string Dialog, string Argument) : NpcTalkMessage;
    /// <summary>Mode 3 sub 5 (OnRewardItems, uitalkcinematic.cpp:1250): i32 count, each i32 item id, u8 rarity, i32 amount, then the item body
    /// when the item data knows the id (CItem::FUN_140523c40; an unknown id reads no body).</summary>
    public sealed record RewardItems(IReadOnlyList<(int ItemId, int Rarity, int Amount)> Items) : NpcTalkMessage;
    /// <summary>Mode 3 sub 6 (OnRewardMerat): i64.</summary>
    public sealed record RewardMerat(long Amount) : NpcTalkMessage;
    /// <summary>Mode 3 sub 7 (OnRewardMeso): i64.</summary>
    public sealed record RewardMeso(long Amount) : NpcTalkMessage;
    /// <summary>Mode 3 sub 8 (OnRewardExp): i32. The Maple2 server writes an i64; the client reads the low half and ignores the rest.</summary>
    public sealed record RewardExp(int Amount) : NpcTalkMessage;
    /// <summary>Mode 3 sub 9 (OnRewardAffinity): i32, u8 jackpot.</summary>
    public sealed record RewardAffinity(int Amount, bool Jackpot) : NpcTalkMessage;
    /// <summary>Mode 3 sub 10 (PlayCutSceneMovie): i32, the movie text.</summary>
    public sealed record Cutscene(int Value, string Movie) : NpcTalkMessage;
    /// <summary>Mode 4 (ModifyScript): the page text, its voice and its illust.</summary>
    public sealed record ModifyScript(string Script, string Voice, string Illust) : NpcTalkMessage;
    /// <summary>Modes and mode 3 subs not decoded here (mode 10 carries a whole CTalkScript).</summary>
    public sealed record Other(int Mode, int Sub) : NpcTalkMessage;

    public static NpcTalkMessage Read(ByteReader r) {
        int mode = r.ReadByte();
        switch (mode) {
            case 0: return new Close();
            case 1: return new Open(r.ReadInt(), r.ReadByte(), r.ReadInt(), r.ReadInt(), r.ReadInt());
            case 2: return new Next(r.ReadByte(), r.ReadInt(), r.ReadInt(), r.ReadInt(), r.ReadInt());
            case 8: return new Reload(r.ReadByte(), r.ReadInt(), r.ReadInt(), r.ReadInt(), r.ReadInt());
            case 9: return new ReloadNoQuest(r.ReadByte(), r.ReadInt(), r.ReadInt(), r.ReadInt());
            case 4: return new ModifyScript(r.ReadUnicodeString(), r.ReadUnicodeString(), r.ReadUnicodeString());
            case 3: {
                int sub = r.ReadByte();
                switch (sub) {
                    case 1: return new NameTag(r.ReadUnicodeString());
                    case 3: return new MovePlayer(r.ReadInt());
                    case 4: return new OpenDialog(r.ReadUnicodeString(), r.ReadUnicodeString());
                    case 5: {
                        int n = r.ReadInt(); var items = new List<(int, int, int)>(n);
                        for (int i = 0; i < n; i++) {
                            int id = r.ReadInt(), rarity = r.ReadByte(), amount = r.ReadInt();
                            if (ItemReader.Known != null && !ItemReader.Known(id)) continue;
                            ItemReader.ReadBodyInto(r, new EquipItem { Id = id, Rarity = rarity, Slot = ItemReader.SlotOf?.Invoke(id) ?? EquipSlot.Unknown });
                            items.Add((id, rarity, amount));
                        }
                        return new RewardItems(items);
                    }
                    case 6: return new RewardMerat(r.ReadLong());
                    case 7: return new RewardMeso(r.ReadLong());
                    case 8: return new RewardExp(r.ReadInt());
                    case 9: return new RewardAffinity(r.ReadInt(), r.ReadByte() != 0);
                    case 10: return new Cutscene(r.ReadInt(), r.ReadUnicodeString());
                    default: return new Other(3, sub);
                }
            }
            default: return new Other(mode, 0);
        }
    }
}

/// <summary>Outbound 0x22 mode 1 (uitalkcinematic.cpp:1432): u8 1, i32 the npc's object id. The talk request.</summary>
public sealed record NpcTalkBeginRequest(int ObjectId) : IOutPacket {
    public Outbound Op => Outbound.NpcTalk;
    public void Write(ByteWriter w) { w.WriteByte(1); w.WriteInt(ObjectId); }
}

/// <summary>Outbound 0x22 mode 0 (UITalkManager::SendNpcTalkMode0 @0x141b1feb0, uitalkcinematic.cpp:1447): u8 0. The player ends the talk.</summary>
public sealed record NpcTalkEndRequest : IOutPacket {
    public Outbound Op => Outbound.NpcTalk;
    public void Write(ByteWriter w) => w.WriteByte(0);
}

/// <summary>Outbound 0x22 mode 2 (UITalkManager::SendNpcTalkMode2 @0x141b20db0, uitalkcinematic.cpp:1676): u8 2, i32 the choice (0 for a
/// plain next). Sent once until the server answers (m_nPending).</summary>
public sealed record NpcTalkNextRequest(int Choice) : IOutPacket {
    public Outbound Op => Outbound.NpcTalk;
    public void Write(ByteWriter w) { w.WriteByte(2); w.WriteInt(Choice); }
}

/// <summary>Outbound 0x22 mode 7 (CQuestAcceptCinematicComponent::SendQuestAccept, cinematiccomponent.cpp:586; CQuestCompleteCinematicComponent::vfunc_2,
/// :652): u8 7, i32 quest, i16 (2 after an accept, 0 after a complete, with the following quest). The talk goes on with that quest's script.</summary>
public sealed record NpcTalkQuestRedirectRequest(int QuestId, short Kind) : IOutPacket {
    public Outbound Op => Outbound.NpcTalk;
    public void Write(ByteWriter w) { w.WriteByte(7); w.WriteInt(QuestId); w.WriteShort(Kind); }
}

/// <summary>Outbound 0x29 mode 2 (CQuestAcceptCinematicComponent::SendQuestAccept @0x14107ecc0, cinematiccomponent.cpp:577): u8 2, i32 quest,
/// i32 the talk npc's object id (UITalkManager::GetNpcId). Starts the quest.</summary>
public sealed record QuestAcceptRequest(int QuestId, int NpcObjectId) : IOutPacket {
    public Outbound Op => Outbound.Quest;
    public void Write(ByteWriter w) { w.WriteByte(2); w.WriteInt(QuestId); w.WriteInt(NpcObjectId); }
}

/// <summary>Outbound 0x29 mode 4 (CQuestCompleteCinematicComponent::vfunc_2 @0x14107f000, cinematiccomponent.cpp:642): u8 4, i32 quest, i32 the
/// talk npc's object id, i32 the reward choice (the client sends 0). Completes the quest; the server grants its rewards.</summary>
public sealed record QuestCompleteRequest(int QuestId, int NpcObjectId, int Choice) : IOutPacket {
    public Outbound Op => Outbound.Quest;
    public void Write(ByteWriter w) { w.WriteByte(4); w.WriteInt(QuestId); w.WriteInt(NpcObjectId); w.WriteInt(Choice); }
}

/// <summary>Outbound 0x29 mode 0x12 (CQuestProgressCinematicComponent::vfunc_2 @0x14107ee60, cinematiccomponent.cpp:611): u8 0x12, i32 quest.
/// Sent from a progress page only for a quest whose data has m_318.m_0 set.</summary>
public sealed record QuestProgressRequest(int QuestId) : IOutPacket {
    public Outbound Op => Outbound.Quest;
    public void Write(ByteWriter w) { w.WriteByte(0x12); w.WriteInt(QuestId); }
}
