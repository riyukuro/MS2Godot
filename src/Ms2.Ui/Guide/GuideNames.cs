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
