namespace Ms2.Ui.Hud;

/// <summary>
/// The scene changes that drop the message box queue and close every shown box (FUN_14151f460(1) = <see cref="MessageBoxQueue.Clear"/>).
/// A field change inside the game scene is not one of them: the UI side of the field load, WorldService::FUN_141bbc1d0
/// (worldservice_1.cpp:3392) -> GFxSceneService::FUN_1422ba150 (gfxsceneservice.cpp:163) -> UISceneEntity::vfunc_53 @0x1422e7e90
/// (uisceneentity.cpp:196), only hands the call on to the shown sub-scene, and neither the game scene nor its play scene
/// overrides it, so a box shown over a same-server field change stays up.
/// STILL-OPEN: UISceneGameCinematicPlay declares a vfunc_53 (uiscenegamecinematicplay.h:33) whose body is not in the decompile.
/// </summary>
public static class SceneChange
{
    /// <summary>CStateService::FUN_1417e25a0 @0x1417e25a0 (statemanager.cpp:1572-1576) on a scene change to another state
    /// (the GameToGame move, networkservice.cpp:953, scene 0x620003b; the state manager's other switches).</summary>
    public static void StateChanged() => MessageBoxQueue.Clear(1);

    /// <summary>CSceneStateGame::OnLeave @0x14170bfd0 (scenestategame.cpp:1318): CUIService::CloseClosableDialogs @0x141adc050
    /// (uiservice.cpp:1496) opens with FUN_14151f460(1); the game scene is left for the character select or the login.</summary>
    public static void GameSceneLeft() => MessageBoxQueue.Clear(1);
}
