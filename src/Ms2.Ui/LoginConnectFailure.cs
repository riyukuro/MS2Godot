namespace Ms2.Ui;

/// <summary>
/// UISceneLogin::vfunc_64 (FUN_141acf470, uiscenelogin.cpp:305-335), what the client does when the connect to the login
/// server failed or timed out: the login dialog's ShowConnectFailure, then the scene's FailConnect (the region list's popup
/// when it is used, <see cref="SceneLoginController.RegionListUsed"/>, then the movie's failConnect).
/// </summary>
public static class LoginConnectFailure
{
    public static void Run(Action showDialogFailure, Action failSceneConnect)
    {
        showDialogFailure?.Invoke();
        failSceneConnect?.Invoke();
    }

    /// <summary>
    /// CNetworkService::OnLoginResult @0x14153c670 (networkservice.cpp:991-1066) for a refusal (result != 0): the result's message
    /// box first, then, on the login scene (0x6200009), UISceneLogin::FUN_141acf470 (networkservice.cpp:1062-1065), the same
    /// failure state as a failed connect (<see cref="Run"/>): the login dialog pops up again with failConnect, so its fields
    /// and its login button take input and the next OnConnect pops it down and connects again (OnEndPopdown -> ConnectTo,
    /// uilogindialog.cpp:190-201: a new connection for each try). Every refusal ends there, the ones with their own boxes too.
    /// </summary>
    public static void AfterRefusal(Action showRefusalBox, Action showDialogFailure, Action failSceneConnect)
    {
        showRefusalBox?.Invoke();
        Run(showDialogFailure, failSceneConnect);
    }
}
