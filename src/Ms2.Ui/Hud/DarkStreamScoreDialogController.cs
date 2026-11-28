namespace Ms2.Ui.Hud;

/// <summary>
/// UIDarkStreamScoreDialog (client/ms2/uidarkstreamscoredialog.cpp, "DarkStreamScoreDialog", UI id 0x4ee4, movie
/// uidarkstreamscoredialog): the points the field game score board's modes 2 and 3 show (<see cref="FieldGameScoreBoard"/>).
/// The ctor (@0x140f35340) sets the score 0, offset 0, align 1, the tween on and SetFlag(1, 1) (<see cref="NotClosable"/>).
/// </summary>
public sealed class DarkStreamScoreDialogController : UiController, IScoreBoardDialog
{
    public const int UiId = 0x4ee4;
    /// <summary>vfunc_43 (@0x140f35480): the entity this many pixels down, plus the offset.</summary>
    public const int TopOffset = 0x82;

    public override string Movie => "uidarkstreamscoredialog";
    protected override string EntityMethod => "setScore";
    /// <summary>GetTypeName (@0x140f35430).</summary>
    public override string TypeName => "DarkStreamScoreDialog";
    /// <summary>SetFlag(1, 1) in the ctor: CloseAllDialogs and the Esc key pass it by.</summary>
    public override bool NotClosable => true;

    int score;              // m_nScore
    int offsetY;            // m_nOffsetY
    int align = 1;          // m_nAlign

    public int Score => score;
    public int OffsetY => offsetY;
    public int Align => align;

    /// <summary>OnLoadComplete (@0x140f35550): UIDialog's, then vfunc_84 of the score.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        SetCur(score);
    }

    /// <summary>vfunc_84 (@0x140f35590): the score, then vfunc_85.</summary>
    public void SetCur(int cur)
    {
        score = cur;
        Refresh();
    }

    /// <summary>vfunc_85 (@0x140f355d0): with the entity, setScore(score, score below 0).</summary>
    public void Refresh()
    {
        if (Host?.Entity == null) return;
        Host.Invoke(Host.Entity, "setScore", score, score < 0);
    }

    /// <summary>SetOffsetY (@0x140f35660): the offset, then the layout again (vfunc_43).</summary>
    public void SetOffsetY(int y)
    {
        offsetY = y;
        Relayout();
    }

    /// <summary>SetAlign (@0x140f356a0): the align, then the layout again (vfunc_43).</summary>
    public void SetAlign(int a)
    {
        align = a;
        Relayout();
    }

    void Relayout()
    {
        if (Host?.Entity == null) return;
        Layout(Host.Player.StageW, Host.Player.StageH);
    }

    /// <summary>vfunc_43 (@0x140f35480): UIDialog's, then the entity at (align, 0, offset + 0x82).</summary>
    public override void Layout(double stageW, double stageH)
    {
        base.Layout(stageW, stageH);
        if (Host?.Entity == null) return;
        Host.Align(Host.Entity, align, 0, offsetY + TopOffset);
    }
}
