using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// The UI half of the party manager's notices (CPartyManager, client/ms2/partymanager.cpp): the chat lines, the centre notices,
/// the message boxes and the sounds its receive handlers raise outside the party book. <see cref="OnApplied"/> takes each
/// decoded message after the book has applied it (the handlers' order: the book's state first, then the line);
/// <see cref="OnNoted"/> takes the lines the book itself raises (<see cref="PartyBook.Noted"/>).
/// STILL-OPEN: the NotifySurvivalState post of mode 0x37 (FUN_140939a10()->Post), BlockFormat(0x32a) of result 0xa (the common
/// string is shown as it is), the expire timer of mode 0x23 (CMyCharacter::SetExpireAndUpdateTimer).
/// </summary>
public sealed class PartyNotices
{
    /// <summary>The chat kinds FUN_1412751b0 is given here (<see cref="MessengerBrowser.TalkKind"/>).</summary>
    const int PartyChat = TalkKind.PartyChat, PartyNoticeKind = TalkKind.PartyNotice, SystemKind = TalkKind.SystemNotice;

    public GameTables Tables;
    /// <summary>DAT_143ab2828: the own character id.</summary>
    public long SelfCharacterId;
    /// <summary>The buddy list (CBuddyManager): a blocked sender's party chat notice is dropped.</summary>
    public BuddyBook Buddies;
    /// <summary>BlindManager::IsBlocked(0, name).</summary>
    public Func<string, bool> IsBlindBlocked = _ => false;
    /// <summary>NoticeController.Text: the string-id text (StringIdInterface::FUN_141024e60).</summary>
    public Func<StringIdText, string> StringIdText = t => t.Text ?? "";
    /// <summary>FUN_1412751b0(text, kind): a chat line.</summary>
    public Action<string, int> ChatLine = (_, _) => { };
    /// <summary>ShowSystemNotice(text, 1, 1): the centre notice and its chat copy.</summary>
    public Action<string> Notice = _ => { };
    /// <summary>PlaySystemSound(name, ""): an empty name plays nothing (FUN_1412760f0).</summary>
    public Action<string> PlaySound = _ => { };
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;
    /// <summary>FindMessengerBrowser(0x4e61)->FUN_141a239e0(name): the invite line; false when the messenger is not open.</summary>
    public Func<string, bool> InviteLine = _ => false;
    /// <summary>The dungeon name of the string table 0x14 (CStringTable::FUN_141811980): null for an unknown id.</summary>
    public Func<int, string> DungeonName = _ => null;

    string Id(int id) => Tables?.Common(id) ?? "";
    string Key(string key) => Tables?.Common(key) ?? "";
    /// <summary>FormatStringId(id, name).</summary>
    string Id(int id, string name) => ClientFormat.Format(Id(id), name ?? "");

    /// <summary>The line of a <see cref="PartyChatNote"/>: GetStringCommon, or FormatStringId with the name, in the party
    /// notice kind; a <see cref="PartyDungeonChangeNote"/> is the centre notice of s_party_change_help_dungeon.</summary>
    public void OnNoted(PartyNote note)
    {
        switch (note)
        {
            case PartyChatNote c:
                ChatLine(c.Name == null ? Id(c.CommonId) : Id(c.CommonId, c.Name), PartyNoticeKind);
                break;
            case PartyDungeonChangeNote d:
                // OnPartyMatchDungeonChange (:1300).
                Notice(ClientFormat.Format(Key("s_party_change_help_dungeon"), DungeonName(d.DungeonId) ?? ""));
                break;
        }
    }

    /// <summary>The handler of <paramref name="m"/> after the book applied it.</summary>
    public void OnApplied(PartyBook book, PartyMessage m)
    {
        switch (m)
        {
            case PartyResult r: OnResult(r.Result, r.Name ?? ""); break;
            case PartyNotice n:
                // OnPartyNotice (:1268): the key formatted with the argument, a party chat line, the sound.
                ChatLine(ClientFormat.Format(Key(n.Key), n.Argument ?? ""), PartyChat);
                PlaySound(n.Sound);
                break;
            case PartyNoticeWithSound n:
                // OnPartyNoticeWithSound (:1281).
                ChatLine(StringIdText(n.Text), PartyChat);
                PlaySound(n.Sound);
                break;
            case PartyDungeonMatchRegister g when g.Announce:
            {
                // OnPartyDungeonMatchRegister (:1318): the line only inside a party, the sound always.
                string name = DungeonGroupName(g.Group);
                if (g.Registered)
                {
                    if (book.IsInParty) ChatLine(ClientFormat.Format(Key("s_party_match_dungeon_register"), name), PartyChat);
                    PlaySound("Dungeon_Room_searchParty");
                }
                else
                {
                    if (book.IsInParty) ChatLine(ClientFormat.Format(Key("s_party_match_dungeon_unregister"), name), PartyChat);
                    PlaySound("Dungeon_Room_searchPartyCancel");
                }
                break;
            }
            case PartyDungeonMatchConfirm:
                // OnPartyDungeonMatchConfirm (:1342): FUN_140d74090, the yes / no box of s_party_match_dungeon.
                ShowBox(MessageBoxQueue.CreateWithCallback(Key("s_party_match_dungeon"), 1, (n, _) => book.AnswerDungeonRoom(n)));
                break;
            case PartyChatNotice c:
                // FUN_14162ee70 (:1357): dropped for a blind-blocked name, a blocked buddy by name or by id.
                if (IsBlindBlocked(c.Name ?? "")) break;
                if (Buddies != null && (Buddies.IsBlockedName(c.Name ?? "") || (c.SenderId != 0 && Buddies.IsBlocked(c.SenderId)))) break;
                ChatLine(StringIdText(c.Text), PartyChat);
                break;
            case PartyFindDungeonCooldown c:
                // OnPartyFindDungeonCooldown (:1425): whole seconds, rounded up by one.
                ChatLine(ClientFormat.Format(Key("s_party_find_dungeon_helper_cooldown"), (uint) c.Milliseconds / 1000 + 1), SystemKind);
                break;
            case PartyInviteSent s:
                // OnPartyInviteSent (:1434): once a minute per name, and only while the messenger browser exists.
                if (book.InviteSentIsNew(s.Name) && InviteLine(s.Name)) book.InviteLineShown(s.Name);
                break;
            case PartySurvivalSquadRegister q when q.Notify:
            {
                // OnPartySurvivalSquadRegister (:1481). STILL-OPEN: the NotifySurvivalState post.
                int channel = book.IsInParty ? PartyChat : PartyNoticeKind;
                ChatLine(Key(q.Registered ? "s_maple_survival_squad_register_squad" : "s_maple_survival_squad_unregister_squad"), channel);
                PlaySound(q.Registered ? "Dungeon_Room_searchParty" : "Dungeon_Room_searchPartyCancel");
                break;
            }
            case PartyDungeonRoomCreate c:
                // OnPartyDungeonRoomCreate (:1503): the members other than the leader get the yes / no box and the sound.
                if (!book.IsLeader(SelfCharacterId))
                {
                    ShowBox(MessageBoxQueue.CreateWithCallback(
                        ClientFormat.Format(Key("s_party_create_dungeon"), DungeonName(c.DungeonId) ?? ""), 1, (n, _) => book.AnswerDungeonRoom(n)));
                    PlaySound("Dungeon_Room_Open");
                }
                break;
        }
    }

    /// <summary>
    /// OnDungeonMatchError (@0x140e146f0, uidungeonhelper.cpp:333): the plain box of the error's common string; errors 2 and 9 show the
    /// formatted text (the parameter, the name) with ShowMessageBoxWithCallback(text, 0, no callback). An error the client's switch
    /// does not list asserts there (uidungeonhelper.cpp:299) and shows nothing.
    /// </summary>
    public void OnDungeonMatchError(DungeonMatchError e)
    {
        switch (e.Error)
        {
            case 1: Box("s_dungeonMatch_error_notFoundDungeon"); break;
            case 2: ShowBox(MessageBoxQueue.CreateWithCallback(ClientFormat.Format(Key("s_dungeonMatch_error_overMaxRegisterUser"), e.Param), 0, null)); break;
            case 3: Box("s_dungeonMatch_error_lackRequire"); break;
            case 4: Box("s_dungeonMatch_error_containCouldNotEnter"); break;
            case 5: Box("s_dungeonMatch_error_insideDungeonUser"); break;
            case 6: Box("s_dungeonMatch_error_isNotChief"); break;
            case 7: Box("s_dungeonMatch_error_hasOfflineUser"); break;
            case 8: Box("s_dungeonMatch_error_hasDungeonMatchCooldown"); break;
            case 9: ShowBox(MessageBoxQueue.CreateWithCallback(ClientFormat.Format(Key("s_dungeonMatch_error_hasDungeonMatchCooldownParty"), e.Name ?? ""), 0, null)); break;
        }
    }

    /// <summary>FUN_140d73ff0(key): a plain box of the common string by key.</summary>
    void Box(string key) => ShowBox(MessageBoxQueue.Create(0, Key(key), null, ""));

    /// <summary>OnPartyResultMessage @0x14162f030 (:1372), the table by result code.</summary>
    void OnResult(int result, string name)
    {
        switch (result)
        {
            case 0xa: Notice(Id(0x32a)); break; // STILL-OPEN: BlockFormat(0x32a)
            case 0x1c: Notice(Key("s_party_err_inviteMe")); break;
            case 0xf: Notice(Id(0x32b, name)); break;
            case 0x4: ChatLine(Id(0x32c), SystemKind); break;
            case 0xe: ChatLine(Id(0x32f), SystemKind); break;
            case 0x9: ChatLine(Id(0x330, name), SystemKind); break;
            case 0x14: ChatLine(Id(0x331, name), SystemKind); break;
            case 0x15: ChatLine(Id(0x332, name), SystemKind); break;
            case 0xc: ChatLine(Id(0x333, name), SystemKind); break;
            case 0xb: ChatLine(Id(0x32e, name), SystemKind); break;
            case 0x5: ChatLine(Id(0x32a, name), SystemKind); break;
            case 0x7: case 0x8: case 0xd: ChatLine(Id(0x329, name), SystemKind); break;
            case 0x2: ChatLine(Id(0x32d, name), SystemKind); break;
            case 0x10: ChatLine(Id(0x336, name), SystemKind); break;
            case 0x11: ChatLine(Id(0x337, name), SystemKind); break;
            case 0x12: ChatLine(Id(0x338, name), SystemKind); break;
            case 0x13: Notice(Id(0x339)); break;
            case 0x18: Notice(Id(0x33a)); break;
            case 0x16: Notice(Id(0x33b)); break;
            case 0x17: ChatLine(Id(0x33c, name), SystemKind); break;
            case 0x19: Notice(Id(0x33d)); break;
            case 0x1a: ChatLine(Id(0x33e, name), SystemKind); break;
            case 0x1b: ChatLine(Id(0x35, name), SystemKind); break;
            case 0x1d: Box("s_room_party_err_is_in_user"); break;
            case 0x1e: Notice(Key("s_party_invite_boss_room")); break;
            case 0x1f: Notice(Key("s_party_err_not_found")); break;
            case 0x20: Notice(ClientFormat.Format(Key("s_party_request_invite"), name)); break;
            case 0x21: Notice(Key("s_party_err_already_vote")); break;
            case 0x22: Notice(Key("s_party_err_vote_need_more_people")); break;
            case 0x23: Notice(Key("s_party_err_vote_cooldown")); break;
            case 0x24: Notice(ClientFormat.Format(Key("s_party_err_vote_cannot_kick_vote"), FormatDungeonGroupList(name))); break;
            case 0x25: Notice(Key("s_party_err_vote_cannot_kick_vote_only_dungeon")); break;
            case 0x26: Notice(Id(0x341)); break;
            case 0x27: Notice(Key("s_party_expel_maple_survival_squad")); break;
            case 0x28: Box("s_dungeonMatch_error_isNotChief"); break;
            case 0x29: Box("s_dungeonMatch_error_hasOfflineUser"); break;
            case 0x2a: Box("s_dungeonMatch_error_insideDungeonUser"); break;
            case 0x2b: Notice(Key("s_party_err_dungeon_match_another")); break;
            case 0x2c: Notice(Key("s_party_err_isNotChief")); break;
            case 0x2d: Notice(Key("s_party_err_hasOfflineUser")); break;
            case 0x2e: Notice(Key("s_party_err_inside_survival")); break;
            case 0x2f: Notice(Key("s_party_err_another_matching")); break;
            case 0x30: Notice(Key("s_party_err_survival_has_solo_register")); break;
            case 0x31: Notice(Key("s_maple_survival_error_squad_register_over_count")); break;
            default: ChatLine(Id(0x979, name), SystemKind); break;
        }
    }

    /// <summary>TranslateEnumDungeonGroup (translateenum.cpp:50): s_enum_dungeon_group_&lt;key&gt;, "" outside the list.</summary>
    string DungeonGroupName(int g) =>
        g > 0 && g < AchieveController.DungeonGroupKeys.Length ? Key("s_enum_dungeon_group_" + AchieveController.DungeonGroupKeys[g]) : "";

    /// <summary>FormatDungeonGroupList @0x141630db0 (:1541): the comma separated group numbers as "a, b".</summary>
    public string FormatDungeonGroupList(string input)
    {
        var result = new System.Text.StringBuilder();
        string[] parts = (input ?? "").Split(',');
        for (int i = 0; i < parts.Length; i++)
        {
            int.TryParse(parts[i].Trim(), out int g);
            result.Append(i == 0 ? "" : ", ").Append(DungeonGroupName(g));
        }
        return result.ToString();
    }
}
