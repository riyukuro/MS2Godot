// Opcode tables for the minimum set. Names are from the client's point of view:
// Inbound = server to client, Outbound = client to server. The restored client's own SendOp/RecvOp enums
// (src/ms2) and the server's enums use the opposite flip of one another; these two do not.
// Values follow the server fork's tables (AGPL-3.0, Maple2) and were checked against recorded KMS traffic.
namespace Ms2.Net;

public enum Inbound : ushort {
    RequestVersion = 0x01,
    RequestKey = 0x04,
    Reconnect = 0x05,
    Ping = 0x06,
    RequestLogin = 0x09,
    LoginResult = 0x0A,
    ServerList = 0x0B,
    CharacterList = 0x0C,
    CharacterCreate = 0x46,
    CharMaxCount = 0xB8,
    MoveResult = 0x0D,
    LoginToGame = 0x0E,
    ResponseTimeSync = 0x11,
    RequestHeartbeat = 0x12,
    RequestClientTickSync = 0x13,
    SyncNumber = 0x14,
    ServerEnter = 0x15,
    RequestFieldEnter = 0x16,
    FieldAddUser = 0x17,
    FieldRemoveUser = 0x18,
    NpcTalk = 0x4B,
    Quest = 0x52,
    Party = 0x53,
    Shop = 0x51,
    GlobalFactor = 0xD6,
    UserSync = 0x1C,
    UserChat = 0x1D,
    UserChatItemLink = 0x1E,
    Notice = 0x71,
    Emote = 0x1F,
    ItemPutOnOrOff = 0x20,
    ItemInventory = 0x21,
    ItemPutOn = 0x25,
    ItemPutOff = 0x26,
    Stat = 0x2E,
    UserBattle = 0x2F,
    Revival = 0x34,
    UserState = 0x36,
    DeadUser = 0xA6,
    ExpUp = 0x37,
    LevelUp = 0x38,
    SkillUse = 0x3C,
    SkillDamage = 0x3D,
    SkillSync = 0x3E,
    SkillCancel = 0x3F,
    StateSkill = 0x41,
    FieldAddNpc = 0x55,
    FieldRemoveNpc = 0x56,
    FieldDeadNpc = 0x57,
    NpcControl = 0x58,
    InteractNpc = 0x59,
    Buff = 0x47,
    UserMoveByPortal = 0x5F,
    StateFallDamage = 0x65,
    KeyTable = 0x6F,
    Job = 0x49,
    SkillPoint = 0x44,
    SkillBookTree = 0x10C,
    ProxyGameObj = 0x7E,
    BadgeEquip = 0x7F,
    RideSync = 0x7A,
    World = 0xB9,
    AttributePoint = 0x45,
    Achieve = 0x5E,
    Mail = 0x54,            // RecvOp Mail (commonfiles/opcodes.h): CSceneStateGame::OnMail
    Prestige = 0x11C,
    Buddy = 0x61,           // RecvOp Buddy (commonfiles/opcodes.h): CSceneStateGame::OnBuddy -> CBuddyManager::OnBuddy
    CharacterAbility = 0xDF,    // RecvOp CharacterAbility (commonfiles/opcodes.h): CCharacterAbilityManager::OnCharacterAbility
    UserEnv = 0xA8,
    HomeCommand = 0xB7,
    Ugc = 0x6B,
    CharacterInfo = 0x78,       // RecvOp::CharacterInfo (commonfiles/opcodes.h)
}

public enum Outbound : ushort {
    ResponseVersion = 0x01,
    ResponseLogin = 0x03,
    ResponseKey = 0x04,
    Pong = 0x06,
    CharacterManagement = 0x09,
    ResponseHeartbeat = 0x0A,
    ItemEquip = 0x16,
    RequestItemInventory = 0x17,
    RequestItemUse = 0x19,
    BadgeEquip = 0x47,
    RequestTimeSync = 0x0B,
    ResponseClientTickSync = 0x0C,
    ResponseFieldEnter = 0x10,
    UserSync = 0x12,
    UserChat = 0x11,
    RequestServerList = 0x14,
    State = 0x80,
    GlobalFactor = 0x82,
    ServerEnter = 0xB0,
    RequestSkillBookTree = 0xA2,
    NpcTalk = 0x22,
    Quest = 0x29,
    // SendOp (commonfiles/opcodes.h): Party, RoomDungeon, DungeonReward, Club
    Party = 0x2C,
    RoomDungeon = 0x2B,
    DungeonReward = 0x9D,
    Club = 0x95,
    Ugc = 0x38,
    RequestCube = 0x37,         // SendOp::RequestCube (commonfiles/opcodes.h)
    RequestHome = 0x51,         // SendOp::RequestHome (commonfiles/opcodes.h)
    // SendOp: the trophy dialog's sends
    Achieve = 0x46,
    Mail = 0x2D,            // SendOp Mail (commonfiles/opcodes.h): the mail dialogs' sends
    Buddy = 0x30,           // SendOp Buddy (commonfiles/opcodes.h): CBuddyManager::SendBuddyRequest, SendBuddyMode20
    EventReward = 0x97,     // SendOp EventReward (commonfiles/opcodes.h): the friend dialog's VIP row (mode 5)
    RequestUserEnv = 0x69,
    Prestige = 0xAC,
    CharacterInfo = 0x1E,       // SendOp::CharacterInfo (commonfiles/opcodes.h)
    CharacterAbility = 0x89,    // SendOp CharacterAbility (commonfiles/opcodes.h): the character ability dialog's sends
}
