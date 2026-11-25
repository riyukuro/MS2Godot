namespace Ms2.Net.Packets;

/// <summary>
/// The guild mode numbers of the GMS2 client (version 12), against the KMS2 client's. Both clients build the guild packet
/// (send 0x4B / GMS2 0x4C, receive 0x8A / GMS2 0x8C) as a u8 mode and a body; GMS2 renumbered the modes. Every guild send
/// writes <see cref="Send"/> and the guild reader translates its type with <see cref="Receive"/>, so the rest of the client
/// keeps the KMS numbers.
/// <para>Send rows: every CGuildManager sender of guildmanager.cpp (39 modes, 38 functions) and the matching function of the
/// GMS2 x64 client (Game Versions/GMS2_64), found by the COutPacket(0x4C) calls (ctor @0x140416060) and paired by their order
/// in the binary and their bodies; the mode is the byte the GMS2 function writes. All 39 rows come from the GMS2 client.
/// The GMS2 server's recv handler agrees on every row it names.</para>
/// <para>Receive rows: the GMS2 guild switch @0x140a91fd0 (u8, jump table @0x140a94fc8, 91 cases) against the KMS switch
/// FUN_1413b6370 (guildmanager.cpp:301, 92 cases). Both list their cases in the same source order, so the cases pair in
/// order; where a case calls its own handler the handlers come in the same order in both binaries. Rows marked "up" also
/// match the name and layout of the GMS2 server's writer.</para>
/// </summary>
public static class GuildModes {
    /// <summary>KMS send mode → GMS2 send mode. Comment: what it is, KMS sender, GMS2 sender.</summary>
    public static readonly IReadOnlyDictionary<byte, byte> Gms2Send = new Dictionary<byte, byte> {
        [1] = 1,        // create: FUN_1413b6d10 / @0x140a951c0
        [2] = 2,        // disband: SendGuildMode2 @0x1413b7020 / @0x140a95420
        [3] = 3,        // invite: FUN_1413b71c0 / @0x140a95570
        [5] = 5,        // answer an invite: SendGuildMode5 @0x1413b74f0 / @0x140a95790
        [7] = 7,        // leave: SendGuildMode7 @0x1413b75a0 / @0x140a958b0
        [8] = 8,        // expel: SendGuildMode8 @0x1413b76c0 / @0x140a959e0
        [10] = 10,      // member grade: SendGuildMode10 @0x1413b78c0 / @0x140a95b50
        [0xd] = 13,     // memo: SendGuildMode13 @0x1413b80d0 / @0x140a96150
        [0xf] = 15,     // attend: SendGuildMode15 @0x1413b8230 / @0x140a962e0
        [0x3f] = 61,    // hand over the master: SendGuildMode63 @0x1413b7ad0 / @0x140a95d00
        [0x40] = 62,    // a guild text: FUN_1413b7c70 / @0x140a95e50
        [0x41] = 63,    // the mark: SendGuildMode65 @0x1413b8a80 / @0x140a96960
        [0x43] = 65,    // a grade's name and rights: SendGuildMode67 @0x1413b8c40 / the mode write @0x140a96d03
        [0x44] = 66,    // propensity: SendGuildMode68 @0x1413b8e60 / @0x140a96dd0
        [0x47] = 69,    // guild mail: SendGuildMail @0x1413bbc10 / @0x140a99330
        [0x4a] = 72,    // pvp history: RequestPvpHistoryAndSeason @0x1413bbe80 / @0x140a99530
        [0x4b] = 73,    // pvp season history: the same two functions' second send
        [0x52] = 80,    // apply to a guild: SendGuildMode82 @0x1413bc4a0 / @0x140a99e90
        [0x53] = 81,    // cancel an application: SendGuildMode83 @0x1413bc540 / @0x140a99f70
        [0x54] = 82,    // answer an application: SendGuildMode84 @0x1413bc5b0 / @0x140a9a030
        [0x55] = 83,    // the guild's applications: SendGuildMode85 @0x1413bc630 / @0x140a9a130
        [0x56] = 84,    // own applications (sent when the window opens outside a guild): SendGuildMode86 @0x1413bc6a0 / @0x140a9a1d0
        [0x57] = 85,    // search by propensity: SendGuildMode87 @0x1413bc710 / @0x140a9a260
        [0x58] = 86,    // search by name: SendGuildMode88 @0x1413bc860 / @0x140a9a3c0
        [0x5a] = 88,    // use a guild skill: FUN_1413b8fe0 / @0x140a96f80
        [0x5b] = 89,    // use a personal guild skill: FUN_1413b96e0 / @0x140a974e0
        [0x5c] = 90,    // upgrade a guild skill: FUN_1413b9ac0 / @0x140a978c0
        [0x63] = 96,    // FUN_1413ba080 / @0x140a97c10
        [0x64] = 97,    // SendGuildMode100 @0x1413ba310 / @0x140a97e60
        [0x65] = 98,    // FUN_1413ba820 / @0x140a98330
        [0x66] = 99,    // FUN_1413babf0 / @0x140a98630
        [0x67] = 100,   // FUN_1413ba4b0 / @0x140a98030
        [0x6d] = 106,   // storage gift: FUN_1413bb490 / @0x140a98d90
        [0x70] = 109,   // storage history: SendGuildMode112 @0x1413bbb80 / @0x140a99280
        [0x71] = 110,   // donate: FUN_1413b8540 / @0x140a965b0
        [0x72] = 111,   // guild npc: FUN_1413bb000 / @0x140a98a10
        [0x73] = 112,   // vs game register: SendGuildMode115 @0x1413bd2e0 / @0x140a9ad20
        [0x74] = 113,   // vs game unregister: SendGuildMode116 @0x1413bd3f0 / @0x140a9ae50
        [0x78] = 117,   // vs game enter: SendGuildMode120 @0x1413c7cb0 / @0x140aa3b10
        // GMS2 mode 64 (@0x140a96af0, no body; the GMS2 server's IncreaseCapacity) has no KMS sender.
    };

    /// <summary>GMS2 receive type → KMS receive type. Comment: the KMS handler, "up" + the GMS2 server's writer when it has one.
    /// GMS2 50 (one i32, the GMS2 server's GuildFunds) has no KMS case: KMS 0x32 carries exp and funds together (GMS2 49 is
    /// the exp alone), and KMS 0x33 / 0x34 (FUN_1413c4b60 / FUN_1413c4bd0) have no GMS2 case.</summary>
    public static readonly IReadOnlyDictionary<byte, byte> Gms2Receive = new Dictionary<byte, byte> {
        [0] = 0,        // OnPacketGuildInfo, up Load
        [1] = 1,        // OnPacketCreateGuild, up Created
        [2] = 2,        // FUN_1413bda00, up Disbanded
        [3] = 3,        // FUN_1413bdaf0, up Invited
        [4] = 4,        // FUN_1413bdbb0, up InviteInfo
        [5] = 5,        // OnPacketInviteResult, up InviteReply
        [6] = 6,        // FUN_1413be150, up NotifyInvite
        [7] = 7,        // FUN_1413be350, up Leave
        [8] = 8,        // FUN_1413be400, up Expelled
        [9] = 9,        // FUN_1413be460, up NotifyExpel
        [11] = 0xb,     // FUN_1413be670, up UpdateMemberMessage
        [10] = 0xa,     // FUN_1413be7e0, up UpdateMemberRank
        [12] = 0xc,     // OnPacketMemberRenamed, up UpdateMemberName
        [14] = 0xe,     // FUN_1413beb10, up Unknown14
        [15] = 0xf,     // OnPacketAttendComplete, up CheckedIn
        [110] = 0x71,   // OnPacketMyDonation, up Donated
        [61] = 0x3f,    // FUN_1413bee70, up UpdateLeader
        [62] = 0x40,    // FUN_1413beed0, up UpdateNotice
        [63] = 0x41,    // FUN_1413beff0, up UpdateEmblem
        [64] = 0x42,    // OnPacketExpandCapacity, up UpdateCapacity
        [65] = 0x43,    // OnPacketGradeChanged, up UpdateRank
        [66] = 0x44,    // FUN_1413bf2a0, up UpdateFocus
        [88] = 0x5a,    // FUN_1413bf340, up UseBuff
        [89] = 0x5b,    // FUN_1413bf540, up UsePersonalBuff
        [95] = 0x61,    // OnPacketGainExpFund, up AddContribution
        [96] = 0x63,    // OnPacketOpenEvent
        [18] = 0x12,    // OnPacketMemberJoined, up Joined
        [19] = 0x13,    // OnPacketMemberLeft, up NotifyLeave
        [20] = 0x14,    // OnPacketMemberExpelled, up NotifyExpelMember
        [33] = 0x21,    // FUN_1413c0250
        [23] = 0x17,    // FUN_1413c0430, up NotifyLogin
        [24] = 0x18,    // FUN_1413c0630, up NotifyLogout
        [21] = 0x15,    // OnPacketMemberGradeChanged, up NotifyUpdateMemberRank
        [22] = 0x16,    // OnPacketSetMemberMessage, up NotifyUpdateMemberMessage
        [36] = 0x24,    // OnPacketMemberContribution, up CheckInTime
        [37] = 0x25,    // OnPacketMemberRaidCount
        [25] = 0x19,    // OnPacketMasterChanged, up NotifyUpdateLeader
        [26] = 0x1a,    // OnPacketNoticeChanged, up NotifyUpdateNotice
        [27] = 0x1b,    // FUN_1413c15b0, up NotifyUpdateEmblem
        [34] = 0x22,    // OnPacketGuildRenamed, up UpdateName
        [28] = 0x1c,    // FUN_1413c1840, up NotifyUpdateCapacity
        [29] = 0x1d,    // OnPacketGradeUpdated, up NotifyUpdateRank
        [30] = 0x1e,    // FUN_1413c1c50, up NotifyUpdateFocus
        [32] = 0x20,    // FUN_1413c1d30, up UpdateMember
        [31] = 0x1f,    // OnPacketMemberDataChanged, up UpdateMemberMap
        [39] = 0x27,    // FUN_1413c2860
        [40] = 0x28,    // FUN_1413c28e0
        [41] = 0x29,    // FUN_1413c2c60
        [42] = 0x2a,    // OnPacketBattleResult
        [43] = 0x2b,    // OnPacketBattleApplied
        [44] = 0x2c,    // OnPacketGuildBattleNotice
        [104] = 0x6b,   // FUN_1413c3110
        [49] = 0x32,    // OnPacketGuildExp, up GuildExperience (GMS2: the exp alone; KMS adds the funds)
        [52] = 0x36,    // FUN_1413c3170, up NotifyUseBuff
        [53] = 0x37,    // FUN_1413c3460, up UpgradeBuff
        [54] = 0x38,    // FUN_1413c3780 (GMS2 handler @0x140aa0090)
        [55] = 0x39,    // FUN_1413c39e0, up UpgradeHouse
        [56] = 0x3a,    // FUN_1413c3c40, up UpdatePoster
        [107] = 0x6e,   // FUN_1413c3e50 (both read an item)
        [57] = 0x3b,    // FUN_1413c3ef0, up UpgradeNpc
        [59] = 0x3d,    // OnPacketWeeklyContributionReset
        [60] = 0x3e,    // FUN_1413c4300 (GMS2 handler @0x140aa0b50)
        [115] = 0x76,   // OnPacketVsGameTicket
        [114] = 0x75,   // OnPacketVsGameMatchCanceled
        [116] = 0x77,   // FUN_1413c45e0 (GMS2 handler @0x140aa0de0)
        [119] = 0x7a,   // FUN_1413c4870
        [120] = 0x7b,   // OnPacketVsGameReward
        [74] = 0x4c,    // OnPacketExpelledOfflineMembers
        [69] = 0x47,    // FUN_1413c4dd0, up SendMail
        [77] = 0x4f,    // FUN_1413c4e20, up Error
        [78] = 0x50,    // FUN_1413c7700 (KMS shares it with 0x4d; GMS2 has its own body)
        [75] = 0x4d,    // FUN_1413c7700, up AddTag
        [76] = 0x4e,    // FUN_1413c7810, up RemoveTag
        [35] = 0x23,    // OnPacketMemberAchievement, up Achievement
        [38] = 0x26,    // FUN_1413c23d0
        [51] = 0x35,    // FUN_1413c2590, up GuildContribution
        [72] = 0x4a,    // OnPacketPvpHistory (GMS2 handler @0x140aa1580)
        [73] = 0x4b,    // OnPacketPvpSeasonHistory (GMS2 handler @0x140aa17b0)
        [80] = 0x52,    // FUN_1413c5360, up SendApplication
        [81] = 0x53,    // FUN_1413c5440, up CancelApplication
        [82] = 0x54,    // OnPacketJoinRequestAnswer, up RespondApplication
        [83] = 0x55,    // OnPacketJoinRequestsForGuild, up ListApplications
        [84] = 0x56,    // FUN_1413c5840, up ListAppliedGuilds
        [85] = 0x57,    // OnPacketSearchResults, up ListGuilds
        [106] = 0x6d,   // OnPacketStorageGiftResult
        [109] = 0x70,   // OnPacketStorageHistory (GMS2 handler @0x140aa2370)
        [48] = 0x30,    // OnPacketMyJoinRequestRemoved, up NotifyApplicant
        [47] = 0x2f,    // FUN_1413c6180, up NotifyApplication
        [45] = 0x2d,    // OnPacketJoinRequestReceived, up ReceiveApplication
        [46] = 0x2e,    // OnPacketJoinRequestWithdrawn, up WithdrawApplication
    };

    /// <summary>The type <see cref="Receive"/> gives a GMS2 type that has no KMS case. No KMS case uses it, so the message
    /// reads as an unhandled type.</summary>
    public const byte NoKmsType = 0xff;

    /// <summary>The wire mode of a KMS send mode for the session's profile.</summary>
    public static byte Send(byte kmsMode, ProtocolProfile profile) => profile.IsGms2 ? Gms2Send[kmsMode] : kmsMode;

    /// <summary>The KMS type of a received wire type for the session's profile (<see cref="NoKmsType"/> for a GMS2 type
    /// without a KMS case).</summary>
    public static byte Receive(byte wireType, ProtocolProfile profile) =>
        !profile.IsGms2 ? wireType : Gms2Receive.TryGetValue(wireType, out byte kms) ? kms : NoKmsType;
}
