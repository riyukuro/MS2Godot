using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIRewardNotify (ms2/client/ms2/uirewardnotify.cpp, "RewardNotify", UI 0x4e5d, movie uirewardnotify): the toasts the
/// movie plays one after the other. Every toast is an object handed to the movie's appendReward (AppendReward
/// @0x141ac1fe0); it is kept back while the movie is not loaded, while the sub scene is 0x6200031 or while the toasts are
/// blocked (m_block), and handed over by vfunc_15's frame check once none of those holds. This port has the merat and red
/// merat notices (ShowMeratNotice @0x141ac0860, ShowMeratRedNotice @0x141ac0990) and the movie's two callbacks
/// (vfunc_28 @0x141abe8b0).
/// </summary>
public sealed class RewardNotifyController : UiController
{
    public const int UiId = 0x4e5d;
    /// <summary>The sub scene id AppendReward and vfunc_15 hold the toasts back in.</summary>
    public const int HeldSubScene = 0x6200031;

    public override string Movie => "uirewardnotify";
    protected override string EntityMethod => "appendReward";
    protected override bool IsDialog => false;          // a UISwfEntity (flag 0x1f in the ctor), not a UIDialog
    public override string TypeName => "RewardNotify";   // GetTypeName @0x141abe240

    readonly List<object> queue = new();                // m_queue

    /// <summary>m_block: BlockToasts (@0x141abf1f0) sets it, UnblockToasts (@0x141abf210) clears it, vfunc_2 clears it on load.</summary>
    public bool Block;

    /// <summary>GFxSceneService::GetSubSceneId.</summary>
    public Func<int> SubSceneId;

    /// <summary>PlaySystemSound(key, "").</summary>
    public Action<string> Sound;

    /// <summary>CUIService::OpenDialog(id): the "onSkillLevelUp" callback opens 0x4f2d.</summary>
    public event Action<int> OpenDialog;

    /// <summary>Toasts waiting in m_queue.</summary>
    public int Pending => queue.Count;

    bool Held => Host?.Entity == null || (SubSceneId?.Invoke() ?? 0) == HeldSubScene || Block;

    /// <summary>AppendReward (@0x141ac1fe0): queue the object while held, else appendReward on the entity.</summary>
    public void AppendReward(object reward)
    {
        if (Held) queue.Add(reward);
        else Host.Invoke("appendReward", reward);
    }

    /// <summary>vfunc_15 (@0x141abe740), each frame: hand over what was kept back once nothing holds it.</summary>
    public void Update()
    {
        if (Held || queue.Count == 0) return;
        var q = queue.ToArray();
        queue.Clear();
        foreach (var r in q) AppendReward(r);
    }

    /// <summary>ShowMeratNotice (@0x141ac0860): msg = FormatStringId(0x685, value), type "merat".</summary>
    public void ShowMeratNotice(long value) => Show(value, "merat");

    /// <summary>ShowMeratRedNotice (@0x141ac0990): the same message, type "merat_red".</summary>
    public void ShowMeratRedNotice(long value) => Show(value, "merat_red");

    void Show(long value, string type) =>
        AppendReward(Host.Obj(("msg", ClientFormat.Format(Model.Tables.Common(0x685), AchieveTexts.Group(value))), ("type", type)));

    /// <summary>vfunc_28: "onReward" with type "achieve" plays system_Trophy_Hero; "onSkillLevelUp" opens 0x4f2d.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (string.Equals(name, "onReward", StringComparison.OrdinalIgnoreCase))
        {
            if (args is { Length: > 0 } && args[0] as string == "achieve") Sound?.Invoke("system_Trophy_Hero");
            return true;
        }
        if (string.Equals(name, "onSkillLevelUp", StringComparison.OrdinalIgnoreCase)) OpenDialog?.Invoke(0x4f2d);
        return false;
    }

    /// <summary>The movie's rewardComplete (RewardComplete @0x141ac1f40).</summary>
    public void RewardComplete() { if (Host?.Entity != null) Host.Invoke("rewardComplete"); }
}
