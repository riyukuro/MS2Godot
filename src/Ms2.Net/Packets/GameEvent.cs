using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>One GameEventShutdownInteractObjectId (commonfiles/gameevent.cpp:4254-4335): the event id, then the
/// (field id, interact object id) pairs it shuts down.</summary>
public sealed record ShutdownInteractObjectEvent(int Id, IReadOnlyList<(uint FieldId, int ObjectId)> Objects);

/// <summary>One GameEventSAHotTime or GameEventPCBangSAHotTime (commonfiles/gameevent.cpp:653-726): the event id, whether it
/// is the PC bang one (its m_e8 = 1), and vfunc_4's first two strings (:705-706), the "," lists of special ability ids
/// (m_68) and values (m_70) vfunc_2 parses.</summary>
public sealed record SpecialAbilityHotTimeEvent(int Id, bool PcBang, string Ids, string Values);

/// <summary>One GameEventPremiumMarketSale (commonfiles/gameevent.cpp:1415-1438): the event id, the discount rate in 1/10000
/// (m_d0), the covered ids (m_d8; 0 covers everything) and the currency item id (m_e8) the context holds (OnStart :1439).</summary>
public sealed record PremiumMarketSaleEvent(int Id, int Rate, IReadOnlyList<int> Ids, int Currency);

/// <summary>One GameEventSaleBeautyShop (commonfiles/gameevent.cpp:787-805): the event id, the beauty shop sale event id
/// (m_d0), the discount rate in 1/10000 (m_d4) and the flag (m_d8) that picks the game rule's list: OnStart / vfunc_6 use list
/// 1 when it is 0 and list 0 otherwise (SetBeautyShopSaleEvent, RemoveBeautyShopSaleEvent).</summary>
public sealed record BeautyShopSaleEvent(int Id, int SaleEventId, int Rate, byte Flag);

/// <summary>One GameEventMesoRevival (commonfiles/gameevent.cpp:869-913): the event id and the bool m_d0 (vfunc_4 :901-905)
/// OnStart (:908-913) puts in the game rule's MesoRevivalEventContext m_18.</summary>
public sealed record MesoRevivalEvent(int Id, bool Flag);

/// <summary>One GameEventMassiveConstructionEvent (commonfiles/gameevent.cpp:2926-2944): the event id, the field ids (m_d0),
/// the channel (m_f0) and the account ids (m_f8) OnStart (:2946-2958) puts in the game rule.</summary>
public sealed record MassiveConstructionEvent(int Id, IReadOnlyList<uint> FieldIds, int Channel, IReadOnlyList<long> AccountIds);

/// <summary>One GameEventIgnoreConstructionLockEvent (commonfiles/gameevent.cpp:4939-4948): the event id and the account ids
/// (m_d0) OnStart (:4952-4958) adds to the game rule's ignored construction lock ids.</summary>
public sealed record IgnoreConstructionLockEvent(int Id, IReadOnlyList<long> AccountIds);

/// <summary>One GameEventReactor (vfunc_4 @0x1403cff10, commonfiles/gameevent.cpp:5498): the event id, then its ReactorContext
/// (Read @0x140402640, gameeventcontext.cpp:1602-1613): m_eventId (the reactor.xml gameEventId it serves), m_28 (the reward
/// count limit), m_endTime, and the field ids it runs in (m_30).</summary>
public sealed record ReactorEvent(int Id, int EventId, int MaxCount, long EndTime, IReadOnlyList<uint> FieldIds);

/// <summary>
/// Inbound GameEvent (RecvOp 0xB2, commonfiles/opcodes.h:185), GameEventManagerCli::Dispatch (@0x140ceab50,
/// gameeventmanagercli.cpp:77): a u8 kind, then
/// 0 (Packet0, the list) and 1 (Packet1, added): i32 count; each: the type name as a unicode string (Create makes
///   GameEvent{name}Cli or GameEvent{name}, :248-255), then that type's vfunc_4 body; each is Added (:211-220);
/// 2 (Packet2, removed): i32 count; each: i32 event id (:171-185);
/// 3 (Packet3, replaced): as 0, each Updated (:236-242).
/// Every entry is read in packet order with its type's vfunc_4 layout (<see cref="ReadBody"/>); kept are
/// GameEventShutdownInteractObjectId's content (vfunc_4, gameevent.cpp:4306-4317: i32 count; each: u32 field id,
/// i32 object id) and the types listed on the init properties. A name no class is registered under makes Create fail: the client asserts and returns with the rest of
/// the packet unread (:144-147); the reader stops there too and sets Truncated.
/// </summary>
public sealed record GameEventMessage(byte Kind, IReadOnlyList<ShutdownInteractObjectEvent> Shutdowns, IReadOnlyList<int> RemovedIds,
    bool Truncated)
{
    public const string ShutdownInteractObjectName = "ShutdownInteractObjectId";

    /// <summary>The SAHotTime and PCBangSAHotTime events of a kind 0, 1 or 3 message, in packet order.</summary>
    public IReadOnlyList<SpecialAbilityHotTimeEvent> HotTimes { get; init; } = Array.Empty<SpecialAbilityHotTimeEvent>();

    /// <summary>The PremiumMarketSale events of a kind 0, 1 or 3 message, in packet order.</summary>
    public IReadOnlyList<PremiumMarketSaleEvent> SaleEvents { get; init; } = Array.Empty<PremiumMarketSaleEvent>();

    /// <summary>The MassiveConstructionEvent and IgnoreConstructionLockEvent events of a kind 0, 1 or 3 message, in packet
    /// order (the two types share the list so their order against each other is kept).</summary>
    public IReadOnlyList<object> ConstructionEvents { get; init; } = Array.Empty<object>();

    /// <summary>The SaleBeautyShop events of a kind 0, 1 or 3 message, in packet order.</summary>
    public IReadOnlyList<BeautyShopSaleEvent> BeautySaleEvents { get; init; } = Array.Empty<BeautyShopSaleEvent>();

    /// <summary>The Reactor events of a kind 0, 1 or 3 message, in packet order.</summary>
    public IReadOnlyList<ReactorEvent> ReactorEvents { get; init; } = Array.Empty<ReactorEvent>();

    /// <summary>The MesoRevival events of a kind 0, 1 or 3 message, in packet order.</summary>
    public IReadOnlyList<MesoRevivalEvent> MesoRevivalEvents { get; init; } = Array.Empty<MesoRevivalEvent>();

    public static GameEventMessage Read(ByteReader r)
    {
        byte kind = r.ReadByte();
        var shutdowns = new List<ShutdownInteractObjectEvent>();
        var removed = new List<int>();
        var hotTimes = new List<SpecialAbilityHotTimeEvent>();
        var saleEvents = new List<PremiumMarketSaleEvent>();
        var construction = new List<object>();
        var beautySales = new List<BeautyShopSaleEvent>();
        var reactors = new List<ReactorEvent>();
        var mesoRevivals = new List<MesoRevivalEvent>();
        bool truncated = false;
        switch (kind)
        {
            case 0:
            case 1:
            case 3:
            {
                int n = r.ReadInt();
                for (int i = 0; i < n; i++)
                {
                    string name = r.ReadUnicodeString();
                    if (!ReadBody(name, r, shutdowns, hotTimes, saleEvents, construction, beautySales, reactors, mesoRevivals))
                    {
                        truncated = true;     // Create failed (:144-147): the client returns here
                        break;
                    }
                }
                break;
            }
            case 2:
            {
                int n = r.ReadInt();
                for (int i = 0; i < n; i++) removed.Add(r.ReadInt());
                break;
            }
        }
        if (!truncated) r.ExpectEnd("GameEvent");
        return new GameEventMessage(kind, shutdowns, removed, truncated) { HotTimes = hotTimes, SaleEvents = saleEvents, ConstructionEvents = construction, BeautySaleEvents = beautySales,
            ReactorEvents = reactors, MesoRevivalEvents = mesoRevivals };
    }

    /// <summary>
    /// The vfunc_4 body of the class registered as "GameEvent" + <paramref name="name"/> (ClassObjectRegistrar: 125 names, and 21 Cli classes Create tries first, of which only TrafficOptimizerCli has its own vfunc_4).
    /// Reader slots (stream.h): ReadStringA is ReadUnicodeStringA, a unicode string on the wire; ReadString (DecodeStrA) is
    /// an ASCII string. Every body but MeratMarketNotice's starts with GameEvent::vfunc_4's i32 id (gameeventbase.cpp:196).
    /// Returns false for a name no class is registered under.
    /// </summary>
    internal static bool ReadBody(string name, ByteReader r, List<ShutdownInteractObjectEvent> shutdowns,
                                  List<SpecialAbilityHotTimeEvent> hotTimes = null, List<PremiumMarketSaleEvent> saleEvents = null,
                                  List<object> construction = null, List<BeautyShopSaleEvent> beautySales = null,
                                  List<ReactorEvent> reactors = null, List<MesoRevivalEvent> mesoRevivals = null)
    {
        switch (name)
        {
            // GameEvent::vfunc_4 only (gameeventbase.cpp:196), the types that don't override it or call only it
            // (FreeAirTaxi gameevent.cpp:1061, OnlinePatch gameeventlobbymap.cpp:184)
            case "DungeonRoomShutdown": case "FieldShutdown": case "FinishPayback": case "GuildChampionship":
            case "GuildEventFund": case "GuildVsGameOpen": case "LoginNotice": case "NpcEventDrop": case "PublicTest":
            case "Shutdown": case "ShutdownBillboard": case "ShutdownDungeonMatch": case "ShutdownNoParam":
            case "ShutdownUGCBanner": case "ShutdownUGCUpload": case "ShutdownShop": case "ShutdownNpcTalk":
            case "ShutdownMastery": case "ShutdownMasteryReward": case "ShutdownEnchantScroll":
            case "ShutdownItemSocketTarget": case "ShutdownItemSocketTargetGemStone": case "ShutdownDesignersMarket":
            case "ShutdownMusicScoreMarket": case "OnlinePatch": case "FreeAirTaxi":
                r.ReadInt();
                return true;

            // GameEventTrafficOptimizerCli::vfunc_4 (gameeventcli.cpp:690): id, 7 x i32
            case "TrafficOptimizer":
                r.ReadInt();
                for (int i = 0; i < 7; i++) r.ReadInt();
                return true;

            // id, 4 x W: DTReward (gameeventdtreward.cpp:82)
            case "DTReward":
                r.ReadInt(); W(r); W(r); W(r); W(r);
                return true;
            // id, 4 x W: SAHotTime and PCBangSAHotTime (gameevent.cpp:701-710): the ids and values kept
            case "SAHotTime": case "PCBangSAHotTime":
            {
                int id = r.ReadInt();
                string ids = r.ReadUnicodeString(), values = r.ReadUnicodeString();
                W(r); W(r);
                hotTimes?.Add(new SpecialAbilityHotTimeEvent(id, name == "PCBangSAHotTime", ids, values));
                return true;
            }

            // AttendGift (gameeventcontext.cpp:424, the context read at :352)
            case "AttendGift":
            {
                r.ReadInt(); r.ReadLong(); r.ReadLong(); W(r); r.ReadString(); r.ReadByte(); r.ReadByte(); r.ReadInt();
                r.ReadByte(); r.ReadInt();
                if (r.ReadByte() != 0) { r.ReadInt(); r.ReadLong(); r.ReadInt(); }
                uint n = (uint)r.ReadInt();
                for (uint i = 0; i < n; i++) Item(r);
                return true;
            }
            // CashAttend (gameeventcontext.cpp:497): id, L, L, I, I, W
            case "CashAttend":
                r.ReadInt(); r.ReadLong(); r.ReadLong(); r.ReadInt(); r.ReadInt(); W(r);
                return true;
            // ShutdownUGCDownload (gameeventshutdown.cpp:296): id, W
            case "ShutdownUGCDownload":
                r.ReadInt(); W(r);
                return true;
            // ReturnUser (gameeventreturnuser.cpp:62), MentoringContext::Read: id, I, L, L, i32 n x I
            case "ReturnUser":
                r.ReadInt(); r.ReadInt(); r.ReadLong(); r.ReadLong(); Ints(r);
                return true;

            // id, I: LobbyMap (gameeventlobbymap.cpp:86), CharacterCountRestricted (:148), ItemBuff (gameevent.cpp:961),
            // SaleEnchant (:1183), UGCMapSaleCoupon (:1359), EventFieldPopup (:1529), the types read as GameEventSale
            // (:1952), GuildSkillSale (:3218), GuildPersonalSkillSale (:3264), GuildExp (:3318), GuildFund (:3364),
            // GuildCoin (:3410), MasteryReduce (:3610), UGCMapContractSale (:3648), UGCMapExtensionSale (:3694),
            // PetBattleExp (:4103), PetComposeSale (:4149), FindDungeonHelperBonus (:4599), StarShop (:4997),
            // AdventureLevelBonusExpRate (:5876), SalePetCrystal (:6621), ArcadeOpen (:6692, ArcadeOpenContext::Read)
            case "LobbyMap": case "CharacterCountRestricted": case "ItemBuff": case "SaleEnchant": case "UGCMapSaleCoupon":
            case "EventFieldPopup": case "SaleGemStoneUpgrade": case "SaleLapenshardUpgradeRGB":
            case "SaleUGCDesign": case "GuildSkillSale": case "GuildPersonalSkillSale": case "GuildExp": case "GuildFund":
            case "GuildCoin": case "MasteryReduce": case "UGCMapContractSale": case "UGCMapExtensionSale":
            case "PetBattleExp": case "PetComposeSale": case "FindDungeonHelperBonus": case "StarShop":
            case "AdventureLevelBonusExpRate": case "SalePetCrystal": case "ArcadeOpen":
                r.ReadInt(); r.ReadInt();
                return true;

            // id, I, W: StringBoard (gameeventstringboard.cpp:70), UserCondition (gameevent.cpp:848), SaleAutoFishing
            // (:1580), SaleAutoPlayInstrument (:1639), LevelUpPackage (:2577), MasteryReward (:3467)
            case "StringBoard": case "UserCondition": case "SaleAutoFishing": case "SaleAutoPlayInstrument":
            case "LevelUpPackage": case "MasteryReward":
                r.ReadInt(); r.ReadInt(); W(r);
                return true;

            // id, W: StringBoardLink (gameeventstringboard.cpp:109), UpdateString (gameevent.cpp:2625), RankDuel (:3075),
            // MeratMarketOpenTab (:3506), WorldmapIcon (:3564)
            case "StringBoardLink": case "UpdateString": case "RankDuel": case "MeratMarketOpenTab": case "WorldmapIcon":
                r.ReadInt(); W(r);
                return true;

            // id, I, I: SaleChat (:618), EnchantRate (:3021), PetMastery (:4048), NpcShopTalk (:5321),
            // NpcShopResetPriceSale (:5425)
            case "SaleChat": case "EnchantRate": case "PetMastery": case "NpcShopTalk": case "NpcShopResetPriceSale":
                r.ReadInt(); r.ReadInt(); r.ReadInt();
                return true;

            case "SaleBeautyShop":      // :787: id, I, I, B
            {
                int id = r.ReadInt(), saleEventId = r.ReadInt(), rate = r.ReadInt();
                beautySales?.Add(new BeautyShopSaleEvent(id, saleEventId, rate, r.ReadByte()));
                return true;
            }
            case "SaleRemake":          // :1995: id, I, B
                r.ReadInt(); r.ReadInt(); r.ReadByte();
                return true;
            case "MesoRevival":         // :900: id, B (ReadBool: nonzero is true)
            {
                int id = r.ReadInt();
                mesoRevivals?.Add(new MesoRevivalEvent(id, r.ReadByte() != 0));
                return true;
            }
            // id, B: TransferItemBound (:3823), DeleteUrlCache (:3984)
            case "TransferItemBound": case "DeleteUrlCache":
                r.ReadInt(); r.ReadByte();
                return true;
            case "DungeonBonusReward":  // :1019, DungeonBonusRewardCtx::Read: id, I, B, F, F, I, i32 n x I
                r.ReadInt(); r.ReadInt(); r.ReadByte(); r.ReadFloat(); r.ReadFloat(); r.ReadInt(); Ints(r);
                return true;
            // id, L: Ontime (:1131), ShutdownDummy (:6563)
            case "Ontime": case "ShutdownDummy":
                r.ReadInt(); r.ReadLong();
                return true;
            case "QuestTag":            // :1245: id, W, L, L
            case "DungeonOpenPeriod":   // :1771: id, W, L, L
                r.ReadInt(); W(r); r.ReadLong(); r.ReadLong();
                return true;
            case "BlackMarketFeeDiscount":   // :1304: id, I, L, L
                r.ReadInt(); r.ReadInt(); r.ReadLong(); r.ReadLong();
                return true;
            case "PremiumMarketSale":   // :1415: id, I, i32 n x I, I
            {
                int id = r.ReadInt(), rate = r.ReadInt();
                int n = r.ReadInt();
                var ids = new List<int>(Math.Max(0, Math.Min(n, 4096)));
                for (int i = 0; i < n; i++) ids.Add(r.ReadInt());
                saleEvents?.Add(new PremiumMarketSaleEvent(id, rate, ids, r.ReadInt()));
                return true;
            }
            case "GameMenuPopup":       // :1482: id, i16, W
                r.ReadInt(); r.ReadShort(); W(r);
                return true;
            case "DungeonExtraReward":  // :1900: id, CAtlMap i32 n x (i32 key, I, I, I)
            {
                r.ReadInt();
                uint n = (uint)r.ReadInt();
                for (uint i = 0; i < n; i++) { r.ReadInt(); r.ReadInt(); r.ReadInt(); r.ReadInt(); }
                return true;
            }
            case "SaleSkillUpgrade":    // :2077: id, 3 x ReadStringA (unicode), W
                r.ReadInt(); W(r); W(r); W(r); W(r);
                return true;
            case "BlueMarble":          // :2234: id, i32 n x (I, I, B, I)
            {
                r.ReadInt();
                uint n = (uint)r.ReadInt();
                for (uint i = 0; i < n; i++) { r.ReadInt(); r.ReadInt(); r.ReadByte(); r.ReadInt(); }
                return true;
            }
            case "MeratMarketNotice":   // :2312: W only, no id
                W(r);
                return true;
            case "DungeonEnterTimeLimit":    // :2362, EventTimeLimitDungeonContext::Read: id, i32 n x (I, I, I, I)
            {
                r.ReadInt();
                uint n = (uint)r.ReadInt();
                for (uint i = 0; i < n; i++) { r.ReadInt(); r.ReadInt(); r.ReadInt(); r.ReadInt(); }
                return true;
            }
            case "SaleHousing":         // :2461: id, i32 n x (i16, i16, I)
            {
                r.ReadInt();
                uint n = (uint)r.ReadInt();
                for (uint i = 0; i < n; i++) { r.ReadShort(); r.ReadShort(); r.ReadInt(); }
                return true;
            }
            case "EpicRestart":         // :2523: id, L, 3 x ReadStringA (unicode)
                r.ReadInt(); r.ReadLong(); W(r); W(r); W(r);
                return true;
            case "StampEvent": case "StampEventChar":     // :2733, StampEventContext::Read
            {
                r.ReadInt(); W(r); r.ReadLong();
                uint n = (uint)r.ReadInt();
                r.ReadInt(); r.ReadByte();
                for (uint i = 0; i < n; i++) { r.ReadInt(); r.ReadByte(); r.ReadInt(); r.ReadInt(); }
                n = (uint)r.ReadInt();
                for (uint i = 0; i < n; i++) { r.ReadInt(); r.ReadByte(); Item(r); Item(r); }
                Item(r);
                return true;
            }
            case "TimeRunEvent":        // :2807, TimeRunEventContext::Read
            {
                r.ReadInt(); r.ReadInt(); r.ReadInt();
                uint n = (uint)r.ReadInt();
                for (uint i = 0; i < n; i++) { r.ReadInt(); r.ReadInt(); r.ReadShort(); }
                Item(r);
                n = (uint)r.ReadInt();
                for (uint i = 0; i < n; i++) { r.ReadInt(); Item(r); }
                return true;
            }
            case "MassiveConstructionEvent":  // :2926: id, i32 n x I, I, i32 n x L; all kept
            {
                int id = r.ReadInt();
                uint n = (uint)r.ReadInt();
                var fields = new List<uint>((int)Math.Min(n, 4096u));
                for (uint i = 0; i < n; i++) fields.Add((uint)r.ReadInt());
                int channel = r.ReadInt();
                n = (uint)r.ReadInt();
                var accounts = new List<long>((int)Math.Min(n, 4096u));
                for (uint i = 0; i < n; i++) accounts.Add(r.ReadLong());
                construction?.Add(new MassiveConstructionEvent(id, fields, channel, accounts));
                return true;
            }
            case "QuestAdditionalReward":     // :3170: id, i16, i16, QuestAdditionalRewardContext::Read
            {
                r.ReadInt(); r.ReadShort(); r.ReadShort();
                for (int i = 0; i < 8; i++) r.ReadInt();
                uint n = (uint)r.ReadInt();
                for (uint i = 0; i < n; i++) { r.ReadInt(); r.ReadInt(); }
                return true;
            }
            case "ConstructShowItem":   // :3754-3766: id, I (m_d0), W (m_d8), i16 (m_e0), u16 n x I
            {
                r.ReadInt(); r.ReadInt(); W(r); r.ReadShort();
                int n = (ushort)r.ReadShort();
                for (int i = 0; i < n; i++) r.ReadInt();
                return true;
            }
            // id, i32 n x I: SaleItemsRestricted (:3873), ShutdownFunctionItem (:4385), ShutdownSkillItem (:4466)
            case "SaleItemsRestricted": case "ShutdownFunctionItem": case "ShutdownSkillItem":
                r.ReadInt(); Ints(r);
                return true;
            case "UgcMapCommendationGM":  // :3935: id, I, L, W
                r.ReadInt(); r.ReadInt(); r.ReadLong(); W(r);
                return true;
            case "ItemBox":             // :4209, ItemBoxEventContext::Read: id, I, I, W, W, W, I
                r.ReadInt(); r.ReadInt(); r.ReadInt(); W(r); W(r); W(r); r.ReadInt();
                return true;
            case ShutdownInteractObjectName:  // :4306-4317
                shutdowns.Add(ReadShutdown(r));
                return true;
            case "ChatFiltering":       // :4675: id, i32 n x W
            {
                r.ReadInt();
                uint n = (uint)r.ReadInt();
                for (uint i = 0; i < n; i++) W(r);
                return true;
            }
            case "BattlePetSpawn":      // :4759: id, B, W
                r.ReadInt(); r.ReadByte(); W(r);
                return true;
            case "Gallery":             // :4831, GalleryContext::Read
            {
                r.ReadInt();
                int n = (ushort)r.ReadShort();
                for (int i = 0; i < n; i++) r.ReadInt();
                n = (ushort)r.ReadShort();
                for (int i = 0; i < n; i++) Item(r);
                r.ReadInt(); W(r); r.ReadLong();
                return true;
            }
            case "IgnoreConstructionLockEvent":   // :4939: id, i32 n x L; all kept
            {
                int id = r.ReadInt();
                uint n = (uint)r.ReadInt();
                var accounts = new List<long>((int)Math.Min(n, 4096u));
                for (uint i = 0; i < n; i++) accounts.Add(r.ReadLong());
                construction?.Add(new IgnoreConstructionLockEvent(id, accounts));
                return true;
            }
            case "Snowman":             // :5072, SnowmanContext::Read
            {
                r.ReadInt(); r.ReadInt(); W(r);
                int n = (ushort)r.ReadShort();
                for (int i = 0; i < n; i++)
                {
                    r.ReadInt(); r.ReadInt(); r.ReadInt();
                    int m = (ushort)r.ReadShort();
                    for (int j = 0; j < m; j++) Item(r);
                    m = (ushort)r.ReadShort();
                    for (int j = 0; j < m; j++) Item(r);
                }
                r.ReadLong();
                W(r);                   // :5075: m_e0, after the context
                return true;
            }
            case "Rps":                 // :5210, RpsContext::Read
            {
                r.ReadInt(); W(r);
                int n = r.ReadInt();
                for (int i = 0; i < n; i++)
                {
                    r.ReadInt();
                    int m = r.ReadInt();
                    for (int j = 0; j < m; j++) Item(r);
                }
                r.ReadInt(); r.ReadInt(); r.ReadLong();
                return true;
            }
            case "NpcShopGivePoint":    // :5380: id, I, B, W
                r.ReadInt(); r.ReadInt(); r.ReadByte(); W(r);
                return true;
            case "Reactor":             // :5498, ReactorContext::Read: id, I, I, L, i32 n x I; all kept
            {
                int id = r.ReadInt();
                int eventId = r.ReadInt();
                int max = r.ReadInt();
                long end = r.ReadLong();
                int n = r.ReadInt();
                var fields = new List<uint>(n > 0 && n < 4096 ? n : 0);
                for (int i = 0; i < n; i++) fields.Add((uint)r.ReadInt());
                reactors?.Add(new ReactorEvent(id, eventId, max, end, fields));
                return true;
            }
            case "CustomTriggerString":  // :5577: id, CRBMap i32 n x (W, W)
            {
                r.ReadInt();
                uint n = (uint)r.ReadInt();
                for (uint i = 0; i < n; i++) { W(r); W(r); }
                return true;
            }
            case "TreeWatering":        // :5651, TreeWateringContext::Read
            {
                r.ReadInt(); r.ReadInt();
                int n = r.ReadInt();
                for (int i = 0; i < n; i++) { r.ReadInt(); r.ReadLong(); r.ReadLong(); }
                n = r.ReadInt();
                for (int i = 0; i < n; i++) { r.ReadLong(); r.ReadLong(); }
                r.ReadInt(); r.ReadInt(); r.ReadByte(); r.ReadInt(); r.ReadInt(); r.ReadInt(); r.ReadByte(); r.ReadInt();
                n = r.ReadInt();
                for (int i = 0; i < n; i++) { r.ReadInt(); r.ReadString(); }
                r.ReadInt(); r.ReadString(); r.ReadInt(); r.ReadString(); r.ReadString(); r.ReadString();
                return true;
            }
            case "BurningLevelup":      // :6042, BurningLevelupContext::Read
            {
                r.ReadInt(); r.ReadInt(); r.ReadLong(); r.ReadInt(); r.ReadInt(); r.ReadInt(); r.ReadInt();
                W(r); W(r); W(r); W(r); r.ReadShort();
                int n = (ushort)r.ReadShort();
                for (int i = 0; i < n; i++) r.ReadInt();
                uint m = (uint)r.ReadInt();
                for (uint i = 0; i < m; i++) Item(r);
                if (r.ReadBool()) { Item(r); r.ReadInt(); r.ReadInt(); W(r); W(r); W(r); }
                return true;
            }
            case "Festival":            // :6100, FestivalContext::Read: id, B, B
                r.ReadInt(); r.ReadByte(); r.ReadByte();
                return true;
            case "CoupleDance":         // :6224, CoupleDanceContext::Read
            {
                r.ReadInt(); r.ReadInt(); r.ReadLong(); r.ReadLong();
                uint n = (uint)r.ReadInt();
                for (uint i = 0; i < n; i++)
                {
                    uint m = (uint)r.ReadInt();
                    for (uint j = 0; j < m; j++) Item(r);
                }
                r.ReadShort();
                n = (uint)r.ReadInt();
                for (uint i = 0; i < n; i++) Item(r);
                r.ReadByte();
                n = (uint)r.ReadInt();
                for (uint i = 0; i < n; i++) W(r);
                n = (uint)r.ReadInt();
                for (uint i = 0; i < n; i++) { W(r); W(r); }
                r.ReadInt();
                return true;
            }
            case "FieldEffect":         // :6292, FieldEffectContext::Read: id, u8 n x I, W
            {
                r.ReadInt();
                int n = r.ReadByte();
                for (int i = 0; i < n; i++) r.ReadInt();
                W(r);
                return true;
            }
            case "ExchangeScrollSale":  // :6349: id, I, i32 n x I
                r.ReadInt(); r.ReadInt(); Ints(r);
                return true;
            case "ItemTradeRestriction":  // :6441: id, I, u16 n x W, u16 n x I
            {
                r.ReadInt(); r.ReadInt();
                int n = (ushort)r.ReadShort();
                for (int i = 0; i < n; i++) W(r);
                n = (ushort)r.ReadShort();
                for (int i = 0; i < n; i++) r.ReadInt();
                return true;
            }
            case "CollectItemGroup":    // :6515, CollectItemGroupContext::Read
            {
                r.ReadInt(); r.ReadInt(); W(r); r.ReadLong(); r.ReadLong();
                int n = r.ReadInt();
                for (int i = 0; i < n; i++) Item(r);
                Item(r);
                return true;
            }
            case "BingoEvent":          // :6760, BingoContext::Read
            {
                r.ReadInt();
                int n = r.ReadInt();
                for (int i = 0; i < n; i++)
                {
                    int m = r.ReadInt();
                    for (int j = 0; j < m; j++) Item(r);
                }
                r.ReadInt(); r.ReadInt();
                return true;
            }
            case "LongTermAttendGift":  // :6898, LongTermAttendGiftContext::Read
            {
                r.ReadInt(); W(r); r.ReadLong(); r.ReadInt(); r.ReadByte();
                uint n = (uint)r.ReadInt();
                for (uint i = 0; i < n; i++) { r.ReadInt(); r.ReadByte(); Item(r); Item(r); }
                r.ReadLong(); r.ReadLong(); W(r); r.ReadInt(); r.ReadInt();
                return true;
            }
            default:
                return false;
        }
    }

    static void W(ByteReader r) => r.ReadUnicodeString();

    /// <summary>CAtlArray/std::vector&lt;unsigned&gt; operator>> (gameevent.cpp:460-555): i32 n, n x i32.</summary>
    static void Ints(ByteReader r)
    {
        uint n = (uint)r.ReadInt();
        for (uint i = 0; i < n; i++) r.ReadInt();
    }

    /// <summary>ItemEntity::Read (commonstruct_wa.cpp:94-102): i32 id, i16 rank, i32 count, 4 bools.</summary>
    static void Item(ByteReader r)
    {
        r.ReadInt(); r.ReadShort(); r.ReadInt();
        r.ReadBool(); r.ReadBool(); r.ReadBool(); r.ReadBool();
    }

    static ShutdownInteractObjectEvent ReadShutdown(ByteReader r)
    {
        int id = r.ReadInt();
        int n = r.ReadInt();
        var objects = new List<(uint, int)>();
        for (int i = 0; i < n; i++)
        {
            uint field = (uint)r.ReadInt();
            int obj = r.ReadInt();
            objects.Add((field, obj));
        }
        return new ShutdownInteractObjectEvent(id, objects);
    }
}
