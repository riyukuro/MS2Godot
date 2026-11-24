namespace Ms2.Ui.Hud;

/// <summary>
/// When the game scene's HUD dialogs open: CUIService::FUN_141ad9e50 @0x141ad9e50 for scene 0x620001d (uiservice.cpp:714-738)
/// returns while WorldService::GetLocalPlayer is null and queues the set (the game menu 0x4e28 among them, :738) only once
/// it is not. The scene's set (UISceneGamePlay::vfunc_48 @0x141ac9f20, uiscenegame.cpp:1041-1066) opens it only with a local
/// player; without one it waits for the MyPC: CSceneStateGame::OnAddUser @0x14170fda0 reads the new MyPC's job system from
/// the packet (scenestategame.cpp:11135), then WorldService::OnObjectCreated (:11214) does SetMyPC and calls
/// UISwfEntityManager::vfunc_2 (worldservice_1.cpp:3128-3141), each shown scene's vfunc_11 (uiswfentitymanager.cpp:427-444),
/// whose UISceneGamePlay::vfunc_11 @0x141aca1a0 (uiscenegame.cpp:1084-1093) runs FUN_141ad9e50 again. So each dialog's
/// OnLoadComplete (the game menu's first RefreshMenu, uigamemenudialog.cpp:855) sees the local player with its level and job.
/// </summary>
public sealed class GameSceneHudOpen
{
    readonly List<Action> pending = new();

    public GameSceneHudOpen(bool hasLocalPlayer) => HasLocalPlayer = hasLocalPlayer;

    /// <summary>WorldService::GetLocalPlayer is set.</summary>
    public bool HasLocalPlayer { get; private set; }

    /// <summary>Opens a dialog of the set: now with a local player, else at <see cref="OnLocalPlayerCreated"/>.</summary>
    public void Open(Action open)
    {
        if (HasLocalPlayer) open();
        else pending.Add(open);
    }

    /// <summary>WorldService::OnObjectCreated for the MyPC: SetMyPC, then the waiting dialogs open, in their order.</summary>
    public void OnLocalPlayerCreated()
    {
        HasLocalPlayer = true;
        var opens = pending.ToArray();
        pending.Clear();
        foreach (var o in opens) o();
    }
}
