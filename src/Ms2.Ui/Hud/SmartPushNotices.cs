using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// What CSceneStateGame::OnSmartPush (scenestategame.cpp:7335) does with a SmartPush packet that is not a plain notice:
/// a positional one (id, value) goes to CSmartPushManager::FUN_140dd86a0 (smartpushmanager.cpp:133), which hands the value
/// to the push with that id (CSmartPush::vfunc_4, smartpush.cpp:309); mode 1 is CUIService::ShowLackMeratAsk
/// (uiservice.cpp:1110). Also the outbound answer of CSmartPush::vfunc_5 (smartpush.cpp:322).
/// </summary>
public sealed class SmartPushNotices
{
    readonly SmartPushTable table;
    readonly Func<string, string> common;
    readonly Func<int, ushort, string> effectName;

    /// <summary>ShowSystemNotice(text, 1, 1).</summary>
    public Action<string> SystemNotice;
    /// <summary>The message box queue's show (FUN_140d73c60).</summary>
    public Action<MessageBoxData> ShowBox;
    /// <summary>CFeatureManager::IsFeatureEnabled(0xc2).</summary>
    public Func<bool> ChargeFeature = () => false;
    /// <summary>The yes answer of the lack-merat box (FUN_141add940): with feature 0xc2 off FUN_140d73cd0 runs, with it on the
    /// platform charge page opens (vfunc_45 of FUN_140d86260). Argument: the feature's state. STILL-OPEN: neither is drawn
    /// here (the charge page is a web page).</summary>
    public Action<bool> ChargeRequested;
    /// <summary>SendOp SmartPush: id, the push's m_n1c, 0, accept.</summary>
    public Action<int, int, bool> SendAnswer;

    public SmartPushNotices(SmartPushTable table, Func<string, string> commonByKey, Func<int, ushort, string> additionalEffectName)
    {
        this.table = table ?? SmartPushTable.Empty;
        common = commonByKey;
        effectName = additionalEffectName;
    }

    /// <summary>FUN_140dd86a0 then CSmartPush::vfunc_4: an additionalEffect push (type 1) says its effect, at level 1, is
    /// active ("s_smartpush_active" with the effect's name); an unknown id or another type shows nothing.</summary>
    public void OnPositional(int id, int value)
    {
        var d = table.Get(id);
        if (d == null || d.ActionType != SmartPushTable.ActionAdditionalEffect) return;
        string name = effectName?.Invoke(d.ActionValue, 1) ?? "";
        SystemNotice?.Invoke(ClientFormat.Format(common("s_smartpush_active"), name));
    }

    /// <summary>ShowLackMeratAsk: a type 1 box with s_err_lack_merat_ask; the yes answer (0) asks for the charge page and
    /// the box closes only when the feature is on (FUN_141add940 returns false with it off, keeping the box).</summary>
    public MessageBoxData ShowLackMeratAsk()
    {
        var box = MessageBoxQueue.CreateWithCallback(common("s_err_lack_merat_ask"), 1, (result, _) =>
        {
            if (result != 0) return true;
            bool on = ChargeFeature?.Invoke() ?? false;
            ChargeRequested?.Invoke(on);
            return on;
        });
        ShowBox?.Invoke(box);
        return box;
    }

    /// <summary>CSmartPush::vfunc_5: the packet answering a push (id, n1c, 0, accept).</summary>
    public void Answer(int id, int n1c, bool accept) => SendAnswer?.Invoke(id, n1c, accept);
}
