namespace Ms2.Ui.Hud;

/// <summary>
/// The job change's screen effect, UIChangeJobEffect (client/ms2/uichangejobeffect.cpp, UI id 0x4f6c, type name
/// "ChangeJobEffect", movie uichangejobeffect), a UIObject, not a dialog. The Job packet's mode 2 for the local player shows
/// it (vfunc_2) with feature 0xca on when the new sub job is a job change's target (CChangeJobTable::IsChangeTarget,
/// scenestategame.cpp:7489-7497). The movie plays the job's symbol and text and calls onHide at its last frame (298),
/// which hides it (vfunc_3).
/// </summary>
public sealed class ChangeJobEffectController : UiController
{
    public override string Movie => "uichangejobeffect";
    protected override string EntityMethod => "alignment";
    protected override bool IsDialog => false;
    public override string TypeName => "ChangeJobEffect";

    /// <summary>vfunc_48 @0x140bd0e80.</summary>
    public const int UiId = 0x4f6c;

    /// <summary>PC::GetSubJob of the local player.</summary>
    public Func<int> SubJob = () => 0;
    /// <summary>vfunc_3 (onHide): the host hides the movie.</summary>
    public event Action Hidden;

    /// <summary>OnLoadComplete @0x140bd0b00: UIObject's, then FUN_140bd0bb0: with the movie and a local player, setData
    /// {jobCode: the sub job, text: s_changejob_effect_text with the sub job's name (string table 6)}.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (Host?.Entity == null || !Model.HasLocalPlayer) return;
        int job = SubJob();
        string text = Ms2.Quest.ClientFormat.Format(Model.Tables.Common("s_changejob_effect_text") ?? "", Model.Tables.JobNameOrEmpty(job));
        Host.Invoke("setData", Host.Obj(("jobCode", job), ("text", text)));
    }

    /// <summary>vfunc_28 @0x140bd0b30: onHide hides it (vfunc_3).</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (!string.Equals(name, "onHide", StringComparison.OrdinalIgnoreCase)) return false;
        Hidden?.Invoke();
        return true;
    }
}
