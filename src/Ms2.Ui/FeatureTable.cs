namespace Ms2.Ui;

/// <summary>
/// CFeatureTable::Init (featuremanager.cpp:126): the client's feature ids and their names, ids 1 to 279 in order. Id 1 is
/// the empty name, which is always on (CFeatureManager::IsFeatureEnabled).
/// </summary>
public static class FeatureTable
{
    /// <summary>The name of id <c>i + 1</c>.</summary>
    public static readonly string[] Names =
    {
        "", "Develop", "KarkarIsland", "MassiveOxQuiz", "TimeAttackDungeon", "AricariDailyQ",
        "KatvanDungeon", "RuneBlader", "FieldEventSpawn", "MusicInstrument", "Fishing", "HabiToken",
        "Darkblossom", "DetectSkillHack", "Barkhant", "CompactControl", "PvPRankingDuelMode", "Striker",
        "MassiveDanceDanceStop", "ItemDropRenewal", "MesoMarket", "ArcadeCrazyMonkeyOcean", "LuaCorrectionPen", "ArcadeSpringFarm",
        "SuperWorldChat", "RewardPreview", "ArcadeSystem", "NutaNuta", "EventBossRush", "EventMapEnterUI",
        "EventDuckyCardGame", "ArcadePapulatus", "TorrentOfDark", "ShadowExpedition", "ShadowCataclysm", "EventSlimeCardGame",
        "SoulBinder", "ExtendLevel55", "ExtendLevel60", "ExtendLevel65", "ExtendLevel70", "ExtendLevel80",
        "ExtendLevel90", "ExtendLevel99", "CharacterAbility", "ItemSocketSystem", "GameDictionary", "JazzInstrument",
        "Kandura", "ChaosPapulatus", "EventMassiveDungeon", "ShadowExpedition2nd", "ShrineOfGuidance", "DoubleImpactStory",
        "itemOptionRenewal", "ItemRePacking", "UGCMapFun", "SkinBreak", "Maview", "PCBang",
        "MeratUI", "VisitorBook", "InGameRank", "RenewalDungeonQuest01", "ContentsGuide01", "EnableGoToNpc",
        "RedMerat", "RedMeratShop", "Gift", "GiftUgc", "GiftPremium", "UGCMarket",
        "PremiumShop", "VIP", "VIPDungeon", "ItemDungeon", "Mastery", "PvpTestSeason",
        "CashShop", "ResetServer", "MustBeModified", "PirateOcean", "SnowKingdom", "DarkCastle",
        "ItemPartyDungeon", "Club", "VIPAirTaxi", "NPCPortal01", "MasterySecond", "MagicScroll01",
        "GuildHouse01", "AdventureBar", "DungeonLimitMesoRevival", "SkinSocket", "WebGuide", "MusicConcert",
        "RedMeratConstruct", "RedMeratUGCDesign", "RedMeratUGCBanner", "QuestChapter00", "MagicScroll03", "GemDamageFont",
        "GemTombStone", "GemSwim", "GemCharEffect", "GemFishingRod", "GemEffect", "GemPetSkin",
        "GemMeta", "QuestProgress", "ItemMerge", "UGCMapOX", "DungeonRank01", "DungeonRank02",
        "GuildSystem01", "BagRenewal01", "InventoryBank", "ReverseRaid01", "QuestNavigation", "GuildMinigame01",
        "GuildRaid01", "HiddenStatAdd01", "HiddenStatAdd02", "EnchantScrollTab01", "RestoreScrollTab01", "FractoScrollTab01",
        "ProtectedScrollTab01", "ChaosRaid", "LiveBroadCast", "VIPGM", "MeratAirTaxi", "GlobalTransferType",
        "GuildBank01", "OptionBlock01", "EnchantBind01", "WebBrowserPopup", "OptionBlockWeapon", "OptionBlockArmor",
        "OptionBlockAccessory", "PetBase", "PetBattle", "Skin2tab", "ItemExtraction", "BlockExpelDungeonRoom",
        "Mentoring", "DailyMission", "FindDungeonHelp", "PetSkillStudy", "FameContents", "PcIllustOption",
        "PetButton01", "PetTrade01", "HappyBirthday", "PetRemake01", "PetRemakeBlock01", "PetRemakeBlock02",
        "AutoMastery02", "SurvivalContents01", "SurvivalContents02", "SurvivalContents03", "SurvivalContentsTeam", "SurvivalObserver",
        "BlackmarketMaxPrice", "ItemRePacking02", "OptionBlock02", "EnchantTransform01", "ItemSocketTransfer01", "QuestPostbox",
        "UGCMarketMusicScore", "WorldBossDungeon", "FameChallengeDungeon", "PremiumMarketPreListening", "RideShareAuthority", "HoldAttack",
        "MouseContoller", "Peachy01", "StatScale_01", "StatScale_02", "StatScale_03", "StatScale_04",
        "GamePad", "EnableCustomColor", "NaColorSystem", "DungeonRewardCountJoin01", "DungeonRewardCountJoin02", "MeratMessage",
        "AchieveReward01", "AchieveRewardMakeUp", "AdventureLevel", "PartyDamageMeter", "TriggerEditorHelpURL", "ConstructExp",
        "UserGeneratedDungeon_01", "CashCharge", "GamePadVibration", "MainMenuText", "UseNumberString", "TriggerVoiceDuration",
        "WorldChampionMushroom", "DungeonSmartMatch", "WaterMark", "JobChange_01", "LapentaDungeon", "EnchantReformation01",
        "OHItemAdditionalLevel01", "FreeSkin2tab", "ItemStoreMoveDisable01", "NextEpicQuestGuide", "EnchantExpansion01", "GuildSkillSystem01",
        "Lapenshard01", "FittingDollEquipAll", "RazorChroma", "GamePadLStickMoveSensitivity", "SplashIncludeCaster", "MushKingRoyaleModeSelect",
        "FoundersPackMode", "SwitchWeaponLR", "HousingLevelhurdle", "DirectQuestFindParty", "HighlightGameMenu", "ChatFormat",
        "StatScore_01", "PartyVoteKick", "JobChange_02", "MouseInteract", "Closet", "HousingDownsize",
        "EnchantDisableBreak01", "Kritias_2018_12", "Newbie", "AutoNewEquipSet", "Blueprint", "EnchantedGearItemBreak01",
        "PetEvolution01", "BlueprintMarket", "InventoryPieceTab", "ChatEmoticon", "FameCompletionTicket", "ColosseumDungeon",
        "MonsterBuffFilter", "ComboKeyCoolTime", "MesoMarketToken", "MesoMarketNA", "LUA_itemRemakeOption", "Discord",
        "ScreenshotWebSend", "MSRebuild", "DungeonFindRenewal01", "Peachy02", "AdventureLevelMission", "PeachyRecommended",
        "ItemSocketExpansion100", "GemStoneUpgrade100", "LapenshardUpgrade100", "UGCMapDelegator", "PetRePackingInfoOpen", "RedMeratMaid",
        "TurkaDungeon", "Wedding", "WeddingBlockSex", "WeddingBlockAge", "MassiveHideAndSeek", "MeratMarketNewBM",
        "Payback", "GachaCapsuleMachine", "RedMeratBeauty", "RedMeratSmartPush", "UnlimitedEnchant", "WaterGunBattle",
        "GemStoneSwap", "GuildPvP", "AdventureProtect", "DeleteAccount", "ProbabilityLogReport", "ProbabilityLogWriteDb",
        "ProbabilityLogUseStatistics", "ProbabilityLogReportForTest", "PersonalInfoAgreement",
    };

    /// <summary>
    /// FUN_141ade430 (uiservice.cpp:3185), Common.m_currentFeatureByString: the name of every enabled id from 1 to 0x117,
    /// each followed by a comma, in id order (CFeatureManager::IsFeatureEnabled, GetFeatureNameById). Id 1 is on, so the
    /// string starts with a comma.
    /// </summary>
    public static string EnabledString(Func<string, bool> enabledByName)
    {
        var sb = new System.Text.StringBuilder();
        for (int id = 1; id < 0x118; id++)
        {
            var name = Names[id - 1];
            if (id != 1 && !enabledByName(name)) continue;
            sb.Append(name).Append(',');
        }
        return sb.ToString();
    }
}
