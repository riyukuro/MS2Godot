namespace Ms2.Ui;

/// <summary>
/// NotifyChangeMyPC (uiserverlistdialog.cpp:203-209): UIServerListDialog::EnterGame posts it when the entered character is not
/// the options' saved one (:643-645). Its listeners forget what they kept for the last character: UIPopupNpcTalkDialog
/// clears its opened quests (FUN_140e771b0, uipopupnpctalkdialog.cpp:248), UILearningQuestDialog its seen marks
/// (FUN_140ea1d70, uilearningquestdialog.cpp:288-291) and UIDailyMissionDialog its auto open flag (OnChangeMyPC,
/// uidailymissiondialog.cpp:291-294).
/// </summary>
public static class NotifyChangeMyPc
{
    /// <summary>Raised on each post.</summary>
    public static event Action Posted;

    public static void Post() => Posted?.Invoke();
}
