namespace Ms2.Ui.Guide;

/// <summary>
/// The guide limits (client/ms2/guideentity_1.cpp, g_guideLimitNames @0x1400f27b0): what a Limit action holds while its
/// guide event runs. The counts live in guideaction.cpp (s_limitCounts[25]).
/// </summary>
public enum GuideLimit
{
    None = 0,
    KeyConfigTab,
    ActionTalk,
    ActionInteraction,
    ItemPutOn,
    ItemPutOff,
    ItemDrop,
    ItemPickup,
    ItemUse,
    PortalUse,
    ShortcutModify,
    ShortcutModifyExceptIndex8,
    ShortcutModifyExceptIndex9,
    CubeKeyInstall,
    CubeKeyRotate,
    CubeKeyRemove,
    CubeKeyUp,
    CubeKeyDown,
    CubeKeySelect,
    CubeKeyAll,
    PlayerMove,
    PlayerControl,
    ProgressAchieve,
    DesignHome,
    Blueprint,
}

/// <summary>The guide trigger kinds (g_guideTriggerNames @0x1400f29e0): what CGuideManager::AddTrigger records for one
/// update and the Trigger condition tests.</summary>
public enum GuideTrigger
{
    None = 0,
    BuyHome,
    SellHome,
    AchieveReward,
    Job,
    UgcShopRegister,
    SkillBookTab,
    RemoveBuilding,
    DarkTombStone,
    Interact,
    ConstructRotate,
    DesignItemCreate,
    ItemPickup,
    WorldmapEventPortal,
    RevivalPanelty,
    FallDamage,
    TaxiFind,
    TaxiAction,
    AddItem,
    AddItemByIconType,
    LearnEmotion,
    ServerTriggerEvent,
    HookUpFish,
    CanAchieveReward,
    UseSkill,
    MovePortal,
    QuestSummonPortal,
    HousePointReward,
    QuestAccept,
    QuestProgress,
    QuestComplete,
    SelectGameMenu,
    ShopOpen,
}

/// <summary>
/// The occurrences CGuideManager::QueueOccurrence takes (the numbers its callers pass). A condition with a kind (its
/// m_n18) is indexed under that number; an occurrence of the number re-queues the guide events that wait on it. A Dialog
/// condition's kind is <see cref="Dialog"/>, but it is indexed by the interned dialog name (CGuideManager::InternOccurrence).
/// </summary>
public enum GuideOccurrence
{
    None = 0,
    FieldId = 1,
    GuideClear = 2,
    Dialog = 3,
    MyPlayerLevel = 4,
    JobCode = 5,
    DesignItem = 6,
    QuestProgress = 7,
    QuestComplete = 8,
    ChatEmoticon = 9,
    EnterDungeon = 10,
    Dead = 11,
    CountItem = 12,
    GuideOpen = 13,
    BattleState = 14,
}

/// <summary>The name lookups of guideentity_1.cpp.</summary>
public static class GuideNames
{
    static readonly string[] Limits = Enum.GetNames<GuideLimit>();
    static readonly string[] Triggers = Enum.GetNames<GuideTrigger>();

    /// <summary>The shop sort names (g_guideShopActionNames @0x1400f2cc0); no guide reader of them is known.</summary>
    public static readonly string[] ShopActions = { "None", "Buy", "Like", "PriceLow", "PriceHigh", "Create", "EnumEnd" };

    /// <summary>The key-config action ids of FUN_141389500 the guide reads: the eight moves (IsSkillRestricted), the action
    /// key (totalAction: Next, UITutorial) and cancel (the cinematic skip key on a gamepad).</summary>
    public const int ActionMoveUp = 0x30d41, ActionMoveDownRight = 0x30d48, ActionTotal = 0x493e2, ActionCancel = 0x49409;

    /// <summary>
    /// FUN_141389500 (guideentity_1.cpp:99): the key-config action id of an action name (the [[keyAction:...]] tags of
    /// the guide texts), <paramref name="fallback"/> when the name is unknown. The names compare as the client's
    /// CStringW::operator== does: case sensitive.
    /// </summary>
    public static int ActionId(string name, int fallback = 0) => name switch
    {
        "moveUp" => 0x30d41,
        "moveDown" => 0x30d42,
        "moveLeft" => 0x30d43,
        "moveRight" => 0x30d44,
        "moveUpLeft" => 0x30d45,
        "moveUpRight" => 0x30d46,
        "moveDownLeft" => 0x30d47,
        "moveDownRight" => 0x30d48,
        "jump" => 0x493e1,
        "totalAction" => 0x493e2,
        "get" => 0x493e3,
        "sit" => 0x493e4,
        "crawl" => 0x493e5,
        "run" => 0x493e6,
        "chat" => 0x493e7,
        "zoomIn" => 0x493e8,
        "zoomOut" => 0x493e9,
        "walkPress" => 0x493ea,
        "install" => 0x493ed,
        "triggerNext" => 0x493ee,
        "screenShot" => 0x493ef,
        "ride" => 0x493f0,
        "transformFittingDoll1" => 0x493f1,
        "transformFittingDoll2" => 0x493f2,
        "transformFittingDoll3" => 0x493f3,
        "transformFittingDoll4" => 0x493f4,
        "transformFittingDoll5" => 0x493f5,
        "changeCameraAngle" => 0x493f6,
        "attack" => 0x49401,
        "catch" => 0x49402,
        "gotoHome" => 0x49403,
        "toggleHeadupDIsplay" => 0x49404,
        "whisper" => 0x49405,
        "itemPreview" => 0x49406,
        "minimapExtendView" => 0x49407,
        "ok" => 0x49408,
        "cancel" => 0x49409,
        "dialogInventory" => 0x61a81,
        "dialogMyInfo" => 0x61a82,
        "dialogSkill" => 0x61a85,
        "dialogMap" => 0x61a86,
        "dialogQuest" => 0x61a87,
        "dialogUGCCamera" => 0x61a88,
        "dialogAchieve" => 0x61a89,
        "dialogBeautyshop" => 0x61a8a,
        "dialogItemEdit" => 0x61a8b,
        "dialogCommunity" => 0x61a8c,
        "dialogAbility" => 0x61a99,
        "dialogMail" => 0x61a8d,
        "dialogGuild" => 0x61a8e,
        "dialogVIP" => 0x61a9a,
        "dialogPartySearch" => 0x61a9b,
        "dialogUGCItemEdit" => 0x61a9c,
        "dialogMastery" => 0x61a9d,
        // the client also maps "dialogQuest" to 0x61a9e after 0x61a87 (guideentity_1.cpp:154); the first one always wins
        "dialogSkillCompactControl" => 0x61a9f,
        "dialogDictionary" => 0x61aa0,
        "dialogGameRanking" => 0x61aa1,
        "dialogClub" => 0x61aa2,
        "dialogPet" => 0x61aa3,
        "dialogLapenshard" => 0x61aa6,
        "actionSlot1" => 0x7a121,
        "actionSlot2" => 0x7a122,
        "actionSlot3" => 0x7a123,
        "actionSlot4" => 0x7a124,
        "actionSlot5" => 0x7a125,
        "actionSlot6" => 0x7a126,
        "actionSlot7" => 0x7a127,
        "actionSlot8" => 0x7a128,
        "actionSlot9" => 0x7a129,
        "actionSlot10" => 0x7a12a,
        "actionSlot11" => 0x7a12b,
        "actionSlot12" => 0x7a12c,
        "actionSlot13" => 0x7a12d,
        "actionSlot14" => 0x7a12e,
        "actionSlot15" => 0x7a12f,
        "actionSlot16" => 0x7a130,
        "actionSlot17" => 0x7a131,
        "actionSlot18" => 0x7a132,
        "actionSlot19" => 0x7a133,
        "actionSlot20" => 0x7a134,
        "actionSlot21" => 0x7a135,
        "actionSlot22" => 0x7a136,
        "actionSkillCompactControl1" => 0xaae61,
        "actionSkillCompactControl2" => 0xaae62,
        "actionSkillCompactControl3" => 0xaae63,
        "gamePadModified1" => 0xc3501,
        "gamePadModified2" => 0xc3502,
        "gamePadModified3" => 0xc3503,
        "gamePadMainMenu" => 0xc3504,
        "gamePadMouseClickRight" => 0xc3505,
        "gamePadMouseClickLeft" => 0xc3506,
        "gamePadMouseMode" => 0xc3507,
        _ => fallback,
    };

    /// <summary>FUN_141389420: the limit of a name (case-insensitive), else <paramref name="fallback"/>.</summary>
    public static GuideLimit ParseLimit(string name, GuideLimit fallback = GuideLimit.None)
    {
        for (int i = 0; i < Limits.Length; i++)
            if (string.Equals(Limits[i], name, StringComparison.OrdinalIgnoreCase)) return (GuideLimit)i;
        return fallback;
    }

    /// <summary>FUN_14138a170: the trigger of a name (case-insensitive), else <see cref="GuideTrigger.None"/>.</summary>
    public static GuideTrigger ParseTrigger(string name)
    {
        for (int i = 0; i < Triggers.Length; i++)
            if (string.Equals(Triggers[i], name, StringComparison.OrdinalIgnoreCase)) return (GuideTrigger)i;
        return GuideTrigger.None;
    }
}
