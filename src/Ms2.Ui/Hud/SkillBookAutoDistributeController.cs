using System.Globalization;
using Ms2.Avm2;
using Ms2.Ui.Skills;

namespace Ms2.Ui.Hud;

/// <summary>
/// The auto distribute window, UISkillBookAutoDistributeDialog (client/ms2/uiskillbookautodistributedialog.cpp, UI id 0x4f20,
/// type name "SkillBookAutoDistributeDialog", movie uiskillbookautodistributedialog), opened by the skill window's auto distribute
/// button with the presets its sub job can use. Up to three buttons (m_distribute1..3) show a preset's name and tooltip; a click
/// (onDistribute, the button number) asks s_msg_skillbook_auto_distribute_msgbox, and its yes answer runs the skill window's
/// ApplyDistribute and closes this window.
/// <para>STILL-OPEN: the skill window's vfunc_55 test before ApplyDistribute (OnMessage @0x140ec4660, meaning not recovered; the
/// window is taken as passing it), the BigData log "skll_auto_distribute", and the modal dialog's own blocking of the screen.</para>
/// </summary>
public sealed class SkillBookAutoDistributeController : UiController
{
    public override string Movie => "uiskillbookautodistributedialog";
    protected override string EntityMethod => "alignment";
    public override string TypeName => "SkillBookAutoDistributeDialog";

    public const int UiId = 0x4f20;
    /// <summary>UpdateDistributeButtons: the buttons m_distribute1..3.</summary>
    public const int Buttons = 3;

    /// <summary>m_names and m_tooltips (the keys of the presets), filled before the movie loads.</summary>
    public IReadOnlyList<AutoDistributePreset> Presets = Array.Empty<AutoDistributePreset>();
    /// <summary>The skill window's ApplyDistribute.</summary>
    public Action<int> ApplyDistribute;
    /// <summary>The box is shown (ShowMessageBox).</summary>
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;

    /// <summary>m_nSelected: the button clicked, from 0.</summary>
    public int Selected { get; private set; }

    string Common(string key, params object[] args) => Ms2.Quest.ClientFormat.Format(Model.Tables.Common(key) ?? "", args);

    /// <summary>OnLoadComplete @0x140ec43d0: UIModalDialog's, then UpdateDistributeButtons.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        UpdateDistributeButtons();
    }

    /// <summary>UpdateDistributeButtons @0x140ec48e0 (:~256): per button of the three: shown when a preset is there, with the name as
    /// its label and, when its tooltip text is not empty, a text tooltip (align 4); then m_bg is 42 per shown button plus 65 high.</summary>
    void UpdateDistributeButtons()
    {
        if (Host.Entity == null || !Model.HasLocalPlayer) return;
        int shown = 0;
        for (int i = 0; i < Buttons; i++)
        {
            if (Host.Child("m_distribute" + (i + 1).ToString(CultureInfo.InvariantCulture)) is not { } button) continue;
            Host.SetVisible(button, true);
            if (Presets.Count <= i) { Host.SetVisible(button, false); continue; }
            Host.Set(button, "label", Common(Presets[i].Name));
            string tip = Common(Presets[i].Tooltip);
            if (tip.Length > 0)
            {
                Host.Set(button, "_tooltipData", Host.Obj(("tooltip", "export_common_tooltip_tooltipText"), ("tooltipAlign", 4), ("text", tip)));
                if (registered.Add(button) && Host.RT.GetClass("scaleform.ms2.manager.TooltipManager") is { } tm)
                    Host.Invoke(tm, "registerMouseEvent", button);
            }
            shown++;
        }
        if (Host.Child("m_bg") is { } bg) Host.Set(bg, "height", (uint)(shown * 0x2a + 0x41));
        Host.Invoke("invalidate");
    }
    readonly HashSet<object> registered = new(ReferenceEqualityComparer.Instance);

    /// <summary>vfunc_28 @0x140ec4400 (:202): onDistribute pops the button number; the preset it names (when there is one) is asked
    /// about in a box of type 1.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (name != "onDistribute") return false;
        if (args.Length == 0) return true;
        Selected = (int)Rt.ToNumber(args[0]) - 1;
        if (Selected >= 0 && Presets.Count > Selected)
            ShowBox(MessageBoxQueue.CreateWithCallback(Common("s_msg_skillbook_auto_distribute_msgbox", Common(Presets[Selected].Name)), 1, OnConfirm));
        return true;
    }

    /// <summary>OnMessage @0x140ec4660 (:224), the box's answer: yes runs ApplyDistribute(m_nSelected) and closes this window; the
    /// box closes either way (the layer takes the next one after the callback).</summary>
    bool OnConfirm(int result, MessageBoxData box)
    {
        if (result != 0) return true;
        ApplyDistribute?.Invoke(Selected);
        Close();
        return true;
    }
}
