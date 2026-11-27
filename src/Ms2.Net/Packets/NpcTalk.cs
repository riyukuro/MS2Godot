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
    /// <summary>
    /// Mode 10 (OnTalkPacket, uitalkcinematic.cpp:1050): i32 the npc's id, i16 sub, then a whole CTalkScript (Deserialize,
    /// talkscript.cpp:216). Sub 1 shows the script's first line as the npc's chat balloon; any other sub opens the special
    /// npc's own talk page (m_nSpecialNpc), which is not ported.
    /// </summary>
    public sealed record SpecialNpc(int NpcId, short Sub, NpcTalkScript Script) : NpcTalkMessage;
    /// <summary>Modes and mode 3 subs not decoded here.</summary>
    public sealed record Other(int Mode, int Sub) : NpcTalkMessage;

    /// <summary>
    /// A CTalkScript read from a packet (CTalkScript::Deserialize, talkscript.cpp:216): the npc's id, its popup state and
    /// chance, whether one content is picked at random, and the contents (CScriptContent::vfunc_6, scriptcontent.cpp:81:
    /// u8 the kind — 1 a balloon line, 0 a cinematic page — then the line, its voice and its illust; a cinematic page adds
    /// its button set and its choices).
    /// </summary>
    public sealed class NpcTalkScript
    {
        public int NpcId;
        public byte PopupState;
        public int PopupProp;
        public bool RandomPick;
        public readonly List<Content> Contents = new();

        /// <summary>One content: a balloon line (kind 1) or a cinematic page (kind 0) with its choices.</summary>
        public sealed class Content
        {
            public byte Kind;
            public string Text = "", Voice = "", Illust = "";
            public int ButtonSet;
            public readonly List<(string Text, string Goto, string GotoFail)> Choices = new();
        }

        public static NpcTalkScript Read(ByteReader r)
        {
            var s = new NpcTalkScript { NpcId = r.ReadInt(), PopupState = r.ReadByte(), PopupProp = r.ReadInt(), RandomPick = r.ReadByte() == 1 };
            int count = r.ReadInt();
            for (int i = 0; i < count; i++)
            {
                var c = new Content { Kind = r.ReadByte() };
                // ReadUnicodeStringA is the same u16 + UTF-16 on the wire, so all three read the same way
                c.Text = r.ReadUnicodeString(); c.Voice = r.ReadUnicodeString(); c.Illust = r.ReadUnicodeString();
                if (c.Kind == 0)
                {
                    c.ButtonSet = r.ReadInt();
                    // CCinematicContent::vfunc_6 (scriptcontent.cpp:385): a u8 flag, then the choices only when it is 1
                    if (r.ReadByte() == 1)
                    {
                        int choices = r.ReadByte();        // CCinematicDistractor::ReadFrom (scriptcontent.cpp:143)
                        for (int j = 0; j < choices; j++)
                            c.Choices.Add((r.ReadUnicodeString(), r.ReadUnicodeString(), r.ReadUnicodeString()));
                    }
                }
                // a kind other than 0 or 1 is read as a balloon line here; the client's Deserialize leaves its content
                // null there and dereferences it (talkscript.cpp:236-240), but a server writes vfunc_1(), which is 0 or 1
                s.Contents.Add(c);
            }
            return s;
        }
    }

    /// <summary>
    /// Mode 3's sub (the u8 OnTalkPacket keeps in m_nSubMode before it dispatches, uitalkcinematic.cpp:1016, for every sub);
    /// null for the other modes.
    /// </summary>
    public static int? SubMode(NpcTalkMessage m) => m switch {
        NameTag => 1, MovePlayer => 3, OpenDialog => 4, RewardItems => 5, RewardMerat => 6, RewardMeso => 7,
        RewardExp => 8, RewardAffinity => 9, Cutscene => 10, Other { Mode: 3 } o => o.Sub,
        _ => null,
    };

    public static NpcTalkMessage Read(ByteReader r) => Read(r, ProtocolProfile.Kms2525);

    /// <summary>The same read for a protocol profile (the mode 3 sub 5 reward items use the profile's item body layout).</summary>
    public static NpcTalkMessage Read(ByteReader r, ProtocolProfile profile) {
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
                            ItemReader.ReadBodyInto(r, new EquipItem { Id = id, Rarity = rarity, Slot = ItemReader.SlotOf?.Invoke(id) ?? EquipSlot.Unknown }, profile);
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
            case 10: return new SpecialNpc(r.ReadInt(), r.ReadShort(), NpcTalkScript.Read(r));
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

/// <summary>Outbound 0x22 mode 6 (UIItemEnchantDialog / UIItemMergeOptionDialog, uiitemenchantdialog.cpp:409,568,746; uiitemmergeoptiondialog.cpp:1030): u8 6,
/// i32 the talk npc's id, i32 the dialog's client constant, i16 the request type, and for the enchant start (type 1) i64 the item uid. Sent while a talk is open;
/// with no talk the client sends mode 4 (<see cref="NpcTalkModifyRequest"/>) instead. The merge dialog adds i16 0xca, i16 its talk id after the type's place.</summary>
public sealed record NpcTalkModifyInTalkRequest(int NpcId, int Constant, short Type, long? ItemUid) : IOutPacket {
    public Outbound Op => Outbound.NpcTalk;
    public void Write(ByteWriter w) { w.WriteByte(6); w.WriteInt(NpcId); w.WriteInt(Constant); w.WriteShort(Type); if (ItemUid is { } u) w.WriteLong(u); }
}

/// <summary>Outbound 0x22 mode 4 (UIItemModifyDialogBase::vfunc_85, uiitemenchantdialog.cpp:409): u8 4, i16 the request type. The same as mode 6 with no open talk.</summary>
public sealed record NpcTalkModifyRequest(short Type) : IOutPacket {
    public Outbound Op => Outbound.NpcTalk;
    public void Write(ByteWriter w) { w.WriteByte(4); w.WriteShort(Type); }
}

/// <summary>Outbound 0x22 mode 0xB (CCustomSelectableDistractorCinematicComponent::vfunc_1 @0x14107fb30, cinematiccomponent.cpp:754): u8 0xB, i32 the talk npc's id, i32 the selection.</summary>
public sealed record NpcTalkDistractorRequest(int NpcId, int Select) : IOutPacket {
    public Outbound Op => Outbound.NpcTalk;
    public void Write(ByteWriter w) { w.WriteByte(0xB); w.WriteInt(NpcId); w.WriteInt(Select); }
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

/// <summary>Outbound 0x44 ResolvePenalty (CPaneltyResolveCinematicComponent::vfunc_2 @0x141080d00, cinematiccomponent.cpp:1082-1083):
/// i32 the talk npc's object id (UITalkManager::GetNpcId). Sent on the penalty-resolve talk page's next, before NpcTalk mode 2.</summary>
public sealed record ResolvePenaltyRequest(int NpcObjectId) : IOutPacket {
    public Outbound Op => Outbound.ResolvePenalty;
    public void Write(ByteWriter w) { w.WriteInt(NpcObjectId); }
}

/// <summary>Outbound 0x45 TakeBoat (CTakeBoatCinematicComponent::vfunc_2 @0x1410812c0, cinematiccomponent.cpp:1159-1161): i32 the talk
/// npc's object id (UITalkManager::GetNpcId), no mode byte. Sent on the take-boat talk page's next, after NpcTalk mode 2.</summary>
public sealed record TakeBoatRequest(int NpcObjectId) : IOutPacket {
    public Outbound Op => Outbound.TakeBoat;
    public void Write(ByteWriter w) { w.WriteInt(NpcObjectId); }
}

/// <summary>Outbound 0x29 mode 0x18 (UILevelUpGuideDialog::FUN_140f857e0 @0x140f857e0, uilevelupguidedialog.cpp:356, and its message box
/// answer SendQuestMode24 @0x140f85290, :285): u8 0x18, i32 quest. The level-up guide's reward claim for a completed guide quest.</summary>
public sealed record QuestGuideRewardRequest(int QuestId) : IOutPacket {
    public Outbound Op => Outbound.Quest;
    public void Write(ByteWriter w) { w.WriteByte(0x18); w.WriteInt(QuestId); }
}

/// <summary>Outbound 0x29 mode 0x10 (UILearningQuestDialog vfunc_28 "onOk" @0x140ea12a0, uilearningquestdialog.cpp:179-181):
/// u8 0x10, i32 learning id (m_18c, the shown row of Table/learningQuest.xml). Starts the learning quest.</summary>
public sealed record QuestLearningRequest(int LearningId) : IOutPacket {
    public Outbound Op => Outbound.Quest;
    public void Write(ByteWriter w) { w.WriteByte(0x10); w.WriteInt(LearningId); }
}

/// <summary>Outbound 0x29 mode 6 (UIQuestDialog abandon, uiquestdialog.cpp:1814 and its message box answer :1828): u8 6, i32 quest.</summary>
public sealed record QuestAbandonRequest(int QuestId) : IOutPacket {
    public Outbound Op => Outbound.Quest;
    public void Write(ByteWriter w) { w.WriteByte(6); w.WriteInt(QuestId); }
}

/// <summary>Outbound 0x29 mode 8 (CQuestManager::FUN_1416d3250 @0x1416d3250, questmanager_2.cpp:2206): u8 8, i32 count, count x i32 quest id.
/// The auto-start and map quests of the field that can start. The client never sends an empty list (questmanager_2.cpp:2205 sends only when the count is > 0): a caller skips that case.</summary>
public sealed record QuestAutoStartRequest(IReadOnlyList<int> QuestIds) : IOutPacket {
    public Outbound Op => Outbound.Quest;
    public void Write(ByteWriter w) { w.WriteByte(8); w.WriteInt(QuestIds.Count); foreach (int id in QuestIds) w.WriteInt(id); }
}

/// <summary>Outbound 0x29 mode 0xC, the quest dialog's onGotoNpc (uiquestdialog.cpp:468; CQuestManager::SendQuestMode12 @0x1416cf510, questmanager_2.cpp:1433): u8 0xC, i32 quest.</summary>
public sealed record QuestGotoNpcRequest(int QuestId) : IOutPacket {
    public Outbound Op => Outbound.Quest;
    public void Write(ByteWriter w) { w.WriteByte(0xC); w.WriteInt(QuestId); }
}

/// <summary>Outbound 0x29 mode 0xD, the quest dialog's onGotoDungeon (uiquestdialog.cpp:478; CQuestManager::SendQuestMode13 @0x1416cf630, questmanager_2.cpp:1446): u8 0xD, i32 quest.</summary>
public sealed record QuestGotoDungeonRequest(int QuestId) : IOutPacket {
    public Outbound Op => Outbound.Quest;
    public void Write(ByteWriter w) { w.WriteByte(0xD); w.WriteInt(QuestId); }
}

/// <summary>CQuestManager::SendQuestMode19 @0x1416cf750 (questmanager_2.cpp:1459): Quest u8 0x13, i32 quest id (the re-entry button).</summary>
public sealed record QuestGotoReEntryRequest(int QuestId) : IOutPacket {
    public Outbound Op => Outbound.Quest;
    public void Write(ByteWriter w) { w.WriteByte(0x13); w.WriteInt(QuestId); }
}

/// <summary>Outbound 0x29 mode 0xE (CQuestManager::SendQuestMode14 @0x1416d0c10, questmanager_2.cpp:1749): u8 0xE, no body. Sent when the player may enter the sky fortress.</summary>
public sealed record QuestSkyFortressRequest : IOutPacket {
    public Outbound Op => Outbound.Quest;
    public void Write(ByteWriter w) => w.WriteByte(0xE);
}

/// <summary>Outbound 0x29 mode 0x14 (CQuestManager::SendQuestMode20 @0x1416d0ea0, questmanager_2.cpp:1795, the yes answer of the quest dispatch box, s_quest_dispatch_yes stringtable.cpp:265): u8 0x14, i32 id, i16 count.</summary>
public sealed record QuestDispatchYesRequest(int Id, short Count) : IOutPacket {
    public Outbound Op => Outbound.Quest;
    public void Write(ByteWriter w) { w.WriteByte(0x14); w.WriteInt(Id); w.WriteShort(Count); }
}

/// <summary>Outbound 0x48 mode 0 (TaxiStationAction::vfunc_1 @0x14095bca0, actionkeyobject.cpp:800-802): u8 0. Sent once per field
/// (the requested list) when the player stands by a station the found-taxi list does not hold.</summary>
public sealed record RequestTaxiRegister : IOutPacket {
    public Outbound Op => Outbound.RequestTaxi;
    public void Write(ByteWriter w) => w.WriteByte(0);
}

/// <summary>Outbound 0x48 mode 1 (StateTaxiCall::vfunc_3 phase 0x69, @0x1417fd690, statetaxicalldata.cpp:276-288): u8 1, then
/// WriteTaxiRequest (@0x1401fe9e0, unit_1401f9eb0_packetpayloadserializers.cpp:1329): i32 station field id, i32 cost. The client
/// always puts 100000000 in the cost (:284); the server prices the ride.</summary>
public sealed record RequestTaxiRide(int TaxiId, int Cost = RequestTaxiRide.ClientCost) : IOutPacket {
    /// <summary>Unk_TaxiReq.m_nCost as StateTaxiCall fills it (statetaxicalldata.cpp:285).</summary>
    public const int ClientCost = 100000000;
    public Outbound Op => Outbound.RequestTaxi;
    public void Write(ByteWriter w) { w.WriteByte(1); w.WriteInt(TaxiId); w.WriteInt(Cost); }
}

/// <summary>Outbound 0x48 mode 3 / 4 (StateCashCall::SendRequest @0x141023650, statecashcalldata.cpp:365-376, a call with no item
/// uid): u8 3 for m_nCallType 1 or 2, u8 4 for m_nCallType 3; then the i32 field id the call goes to (StateCashCallData m_nPrice is filled from the manager's field id argument, TakeCashTaxi @0x141b5aa70 -> cashcallmanager.cpp:149).</summary>
public sealed record RequestTaxiCash(int CallType, int FieldId) : IOutPacket {
    public Outbound Op => Outbound.RequestTaxi;
    public void Write(ByteWriter w) {
        if (CallType == 3) w.WriteByte(4);
        else if (CallType == 1 || CallType == 2) w.WriteByte(3);
        else throw new ArgumentOutOfRangeException(nameof(CallType), "StateCashCall::SendRequest sends only call types 1, 2 and 3");
        w.WriteInt(FieldId);
    }
}

/// <summary>Outbound 0x48 mode 5 (CAutoFindTaxiStation::SendRequestTaxiMode5 @0x1409f1890, autofindtaxistation.cpp:294-302, and the
/// escape branch :379-381): u8 5. The auto find that sends it is not ported.</summary>
public sealed record RequestTaxiAutoFind : IOutPacket {
    public Outbound Op => Outbound.RequestTaxi;
    public void Write(ByteWriter w) => w.WriteByte(5);
}
