using System;

namespace Ms2.Ui;

/// <summary>
/// The shell movie's depth layers (scaleform.ms2.common.CommonDepth, DEPTH_LAYER_*). Every movie the client loads goes into
/// one of them: UISwfEntity passes its layer (m_2c, 0 unless the dialog's constructor sets it, uiswfentity.cpp:39) to
/// MovieClipLoader.loadSwf, and msLoader adds the movie to it (CommonDepth.addLayer).
/// </summary>
public static class CommonDepth
{
    public const int Default = 0, DamageEffect = 1, NameTag = 2, HpBar = 3, Emoticon = 4, DropIcon = 5, Monologue = 6,
        ChatBalloon = 7, Damage = 8, ActionGuide = 9, ScreenMask = 10, UpperHud = 11, Scene = 12, LowerBuiltinDialog = 13,
        BuiltinDialog = 14, ExpBar = 15, AdventureLevelExpBar = 16, Party = 17, UpperHpBar = 18, Timer = 19, KillCount = 20,
        AchieveEffect = 21, FieldName = 22, LobbyClearScreen = 23, SkillHolder = 24, Dialog = 25, PopupDialog = 26,
        CinematicDialog = 27, Cinematic = 28, PopupMenu = 29, MassiveEvent = 30, HeadUpDisplay = 31, CubeHolder = 32,
        Cutscene = 33, PopupModal = 34, GuideEvent = 35, SystemText = 36, Tooltip = 37, TopLayerNotify = 38,
        MouseCursor = 39, Matinee = 40, ClearScreen = 41, Max = 42;
}

/// <summary>
/// Which of the <see cref="CommonDepth"/> layers are drawn. CUIService::SetMovieLayerVisible (0x141add1b0) calls
/// GFxManager::SetMoviesLayerVisible (gfxmanager.cpp:402), which has every movie's MovieClipLoader run setVisibleLayer:
/// CommonDepth.setVisibleLayer sets the layer sprite's visible, so every movie in that layer is drawn or not. The layers
/// start visible.
/// </summary>
public sealed class MovieLayers
{
    readonly bool[] visible = new bool[CommonDepth.Max];

    public MovieLayers() { Array.Fill(visible, true); }

    /// <summary>A layer's visible changed: the layer and its new value.</summary>
    public event Action<int, bool> Changed;

    /// <summary>CFeatureManager::IsFeatureEnabled; null: every feature off.</summary>
    public Func<int, bool> FeatureEnabled;

    public bool IsVisible(int layer) => layer < 0 || layer >= visible.Length || visible[layer];

    /// <summary>CUIService::SetMovieLayerVisible. setVisibleLayer ignores a layer CommonDepth has no sprite for.</summary>
    public void SetVisible(int layer, bool on)
    {
        if (layer < 0 || layer >= visible.Length || visible[layer] == on) return;
        visible[layer] = on;
        Changed?.Invoke(layer, on);
    }

    /// <summary>
    /// The field's layers (CUIService FUN_141adb4c0, uiservice.cpp:1808). UISceneGamePlay hides them as it leaves for a
    /// cinematic scene (vfunc_49: FUN_141acc6c0 is 1 for scenes 0x620001f, 0x6200022, 0x6200031, 0x6200041, 0x620007f)
    /// and shows them as it comes back from one (vfunc_48). The adventure level bar only with feature 0xbd
    /// (AdventureLevel).
    /// </summary>
    public void SetFieldLayersVisible(bool on)
    {
        SetVisible(CommonDepth.Dialog, on);
        SetVisible(CommonDepth.Party, on);
        SetVisible(CommonDepth.Timer, on);
        SetVisible(CommonDepth.PopupDialog, on);
        SetVisible(CommonDepth.LowerBuiltinDialog, on);
        SetVisible(CommonDepth.BuiltinDialog, on);
        SetVisible(CommonDepth.HpBar, on);
        SetVisible(CommonDepth.ExpBar, on);
        if (FeatureEnabled?.Invoke(Feature.AdventureLevel) == true) SetVisible(CommonDepth.AdventureLevelExpBar, on);
        SetVisible(CommonDepth.Damage, on);
        SetVisible(CommonDepth.Matinee, on);
        SetVisible(CommonDepth.Tooltip, on);
        SetVisible(CommonDepth.Default, on);
        SetVisible(CommonDepth.NameTag, on);
        SetVisible(CommonDepth.Emoticon, on);
        SetVisible(CommonDepth.DropIcon, on);
        SetVisible(CommonDepth.KillCount, on);
        SetVisible(CommonDepth.Monologue, on);
        SetVisible(CommonDepth.FieldName, on);
        SetVisible(CommonDepth.PopupMenu, on);
        SetVisible(CommonDepth.SystemText, on);
        SetVisible(CommonDepth.UpperHpBar, on);
        SetVisible(CommonDepth.ScreenMask, on);
        SetVisible(CommonDepth.ActionGuide, on);
        SetVisible(CommonDepth.ChatBalloon, on);
        SetVisible(CommonDepth.DamageEffect, on);
        SetVisible(CommonDepth.MassiveEvent, on);
        SetVisible(CommonDepth.AchieveEffect, on);
        SetVisible(CommonDepth.HeadUpDisplay, on);
        SetVisible(CommonDepth.LobbyClearScreen, on);
        SetVisible(CommonDepth.GuideEvent, on);
        SetVisible(CommonDepth.UpperHud, on);
    }

    /// <summary>
    /// The field gives way to the NPC talk scene (0x6200031): UISceneGamePlay::vfunc_49 hides the field's layers, then
    /// UISceneGameCinematicTalk::vfunc_48 (uiscenegame.cpp:849) opens the scene: CUIService FUN_141ada6f0 shows the layers
    /// the talk draws in (uiservice.cpp:834), and the emoticon layer is shown.
    /// </summary>
    public void EnterTalk()
    {
        SetFieldLayersVisible(false);
        SetVisible(CommonDepth.SystemText, true);
        SetVisible(CommonDepth.Tooltip, true);
        SetVisible(CommonDepth.Cutscene, true);
        SetVisible(CommonDepth.Cinematic, true);
        SetVisible(CommonDepth.ScreenMask, true);
        SetVisible(CommonDepth.DamageEffect, true);
        SetVisible(CommonDepth.CinematicDialog, true);
        SetVisible(CommonDepth.Emoticon, true);
    }

    /// <summary>
    /// The talk scene closes: UISceneGameCinematicTalk::vfunc_49 (uiscenegame.cpp:866) hides the emoticon layer (its
    /// CUIService FUN_141adad20 hides the talk dialog, no layer), then UISceneGamePlay::vfunc_48 shows the field's layers.
    /// </summary>
    public void LeaveTalk()
    {
        SetVisible(CommonDepth.Emoticon, false);
        SetFieldLayersVisible(true);
    }
}
