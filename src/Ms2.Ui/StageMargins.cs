namespace Ms2.Ui;

/// <summary>
/// The HUD's reserved bottom area (uibuiltindialog.cpp:40-70): the globals DAT_1439f8ccc (bottom bar height, 21 in the data),
/// DAT_1439f8cd0 (adventure level bar height, 21) and DAT_1439f8cc8 (screen margin, 193). STILL-OPEN: whether the client changes
/// them at run time (no writer was found in the decompile).
/// </summary>
public static class StageMargins
{
    public const int BottomBarGlobal = 21, AdventureBarGlobal = 21, ScreenMarginGlobal = 193;

    /// <summary>The adventure level bar's feature (CFeatureManager id 0xbd, "AdventureLevel").</summary>
    public const int AdventureLevelFeature = 0xbd;

    /// <summary>FUN_1418dd0c0 (@0x1418dd0c0): the bottom bar's height less 2.</summary>
    public static int BottomBar => BottomBarGlobal - 2;

    /// <summary>FUN_1418dd110 (@0x1418dd110): 0 without the adventure level feature, else its bar's height less 2.</summary>
    public static int AdventureBar(bool adventureLevel) => adventureLevel ? AdventureBarGlobal - 2 : 0;

    /// <summary>The value FUN_141ade430 (uiservice.cpp:3196) gives Common.m_bottomMarginOfStage: FUN_1418dd0c0 + FUN_1418dd110, the
    /// area the exp bars use. Common.getBottomMarginOfStage returns 18 instead without the AdventureLevel feature.</summary>
    public static int BottomOfStage(bool adventureLevel) => BottomBar + AdventureBar(adventureLevel);

    /// <summary>FUN_1418dd0d0 (@0x1418dd0d0): the value of Common.m_bottomMarginOfGamePlayScene (uiservice.cpp:3197).</summary>
    public static int BottomOfGamePlayScene(bool adventureLevel) => BottomOfStage(adventureLevel) + ScreenMarginGlobal;
}
