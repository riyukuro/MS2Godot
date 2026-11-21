namespace Ms2.Ui;

/// <summary>
/// UISceneLogin::vfunc_64 (FUN_141acf470, uiscenelogin.cpp:305-335), what the client does when the connect to the login
/// server failed or timed out: the login dialog's ShowConnectFailure, then the scene movie's failConnect.
/// STILL-OPEN: the region list call between the two (IsRegionListUsed, UIRegionListDialog) is not ported with the region list.
/// </summary>
public static class LoginConnectFailure
{
    public static void Run(Action showDialogFailure, Action failSceneConnect)
    {
        showDialogFailure?.Invoke();
        failSceneConnect?.Invoke();
    }
}
