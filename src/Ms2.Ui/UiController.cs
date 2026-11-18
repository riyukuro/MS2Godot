using Ms2.Avm2;

namespace Ms2.Ui;

/// <summary>
/// One UI movie's game-side controller (the client's UISwfEntity / UIDialog subclass). Overrides mirror the client's
/// virtuals: <see cref="OnLoadComplete"/>, <see cref="OnCallback"/> (vfunc_28), <see cref="GetUserData"/> (vfunc_45),
/// <see cref="OnStatus"/> (UIStatusEventListener vfunc_oa0_3+N), <see cref="Layout"/> (vfunc_43).
/// </summary>
public abstract class UiController
{
    public UiHost Host { get; internal set; }
    public UiModel Model => Host.Model;
    protected PlayerState Pc => Host.Model.Player;

    /// <summary>The movie file name without extension ("UI" + GetTypeName(), lower case).</summary>
    public abstract string Movie { get; }

    /// <summary>A method only the driven object has; the host takes the first display object with it as m_30.</summary>
    protected abstract string EntityMethod { get; }

    /// <summary>UIDialog subclasses open with the movie's tween (UIDialog::OnLoadComplete invokes startOpen, uidialog.cpp:704).</summary>
    protected virtual bool IsDialog => true;

    public virtual AsObject FindEntity() => Host.Player.FindOwner(EntityMethod);

    /// <summary>
    /// UIObject vfunc_43, on load and on resize (stage units). UIDialog's (uidialog.cpp:822) re-aligns the dialog through
    /// ASGameEntity::vfunc_25 = Invoke("alignment") (asgameentity.cpp:26), except that a dialog placed from its settings
    /// file (m_78) that has a drag bar and was not marked for re-alignment (m_60) keeps its place while its top left corner
    /// is on the stage (0 to the stage width / height, ASDisplayObject::FUN_1422c1500 / 1530).
    /// </summary>
    public virtual void Layout(double stageW, double stageH)
    {
        if (IsDialog && !realign && placedFromFile && HasDragBar)
        {
            var (x, y) = Position();
            if ((float)stageW >= x && (float)stageH >= y && x >= 0f && y >= 0f) return;
        }
        if (Host.Player.FindOwner("alignment") is { } al) Host.Invoke(al, "alignment");
    }

    // ---- UIDialog: the dialog's place and its settings file (uidialog.cpp) ---------------------------------------------

    /// <summary>GetTypeName (UIDialog: "Dialog"): the settings file's name and root element.</summary>
    public virtual string TypeName => "Dialog";

    /// <summary>m_74 (editAlign): set when the player moved the dialog (vfunc_77).</summary>
    public int EditAlign;
    bool placedFromFile;       // m_78
    bool realign;              // m_60 (UISwfEntity): set by a saved rate above 1, cleared after the load's layout
    bool dragging;             // m_70
    int closeState = 2;        // m_c0: 0 open, 1 closing (the close tween), 2 closed
    bool closed = true;        // m_68

    /// <summary>The dialog finished closing (OnEndClose, uidialog.cpp:893: Unload): the host drops the movie.</summary>
    public event Action Unloaded;

    internal bool IsDialogEntity => IsDialog;

    /// <summary>vfunc_55 (uidialog.cpp:487): closed (m_68) with the close finished (m_c0 2), not while the close tween runs.</summary>
    public bool IsClosed => closed && closeState == 2;

    /// <summary>vfunc_52 (uidialog.cpp:220): the dialog has a m_dragBar child.</summary>
    bool HasDragBar => Host.Child("m_dragBar") != null;

    /// <summary>ASDisplayObject::GetPosition of m_30 (asdisplayobject.cpp:465).</summary>
    (float X, float Y) Position() =>
        ((float)Rt.ToNumber(Host.Get(Host.Entity, "x")), (float)Rt.ToNumber(Host.Get(Host.Entity, "y")));

    /// <summary>ASDisplayObject::SetPositionFloat (asdisplayobject.cpp:195) of m_30.</summary>
    void SetPosition(float x, float y) { Host.Set(Host.Entity, "x", (double)x); Host.Set(Host.Entity, "y", (double)y); }

    /// <summary>
    /// The settings file's other members (a subclass's vfunc_53 after UIDialog's: UIQuestDialog writes its tab filters).
    /// </summary>
    protected virtual void SaveSettings(DialogSettings s) { }

    /// <summary>A subclass's vfunc_54 after UIDialog's.</summary>
    protected virtual void LoadSettings(DialogSettings s) { }

    /// <summary>
    /// vfunc_4 / vfunc_53 (uidialog.cpp:261, :296): with the movie loaded (m_4c) and a character folder, the file is written
    /// again from scratch: with a drag bar transform.editAlign, then transform.x and y (the dialog's position), then with a
    /// drag bar transform.rateX / rateY, the position over the client size times the UI scale (DAT_143af62d4 / 62d8 times
    /// FUN_141276490). m_7c is 1 for every dialog here (UIDialog ctor, UIQuestDialog :295), so no width and height.
    /// </summary>
    public void SaveTransform()
    {
        if (!IsDialog || Host.Entity == null || Host.Folder == null) return;
        var s = new DialogSettings(TypeName);
        bool bar = HasDragBar;
        if (bar) s.Set("transform", "editAlign", EditAlign);
        var (x, y) = Position();
        s.Set("transform", "x", x);
        s.Set("transform", "y", y);
        if (bar)
        {
            s.Set("transform", "rateX", x / (float)Host.ClientStageW);
            s.Set("transform", "rateY", y / (float)Host.ClientStageH);
        }
        SaveSettings(s);
        Host.Folder.Write(TypeName + ".xml", s.ToString());
    }

    /// <summary>
    /// vfunc_5 / vfunc_54 (uidialog.cpp:279, :324), before the load's layout: a file with the type's root element marks the
    /// dialog placed from its file (m_78). With a drag bar it reads editAlign and the position; a saved rateX or rateY of 0
    /// or more places it at the rates times the client size times the UI scale instead (both, the missing one as -1), and a
    /// rate above 1 marks it for re-alignment (m_60).
    /// </summary>
    public virtual void LoadTransform()
    {
        if (!IsDialog || Host.Folder == null) return;
        var s = DialogSettings.Parse(TypeName, Host.Folder.Read(TypeName + ".xml"));
        if (s == null) return;
        if (HasDragBar && Host.Entity != null)
        {
            s.Get("transform", "editAlign", ref EditAlign);
            float x = 0f, y = 0f, rateX = -1f, rateY = -1f;
            s.Get("transform", "x", ref x);
            s.Get("transform", "y", ref y);
            s.Get("transform", "rateX", ref rateX);
            s.Get("transform", "rateY", ref rateY);
            if (rateX >= 0f || rateY >= 0f)
            {
                x = rateX * (float)Host.ClientStageW;
                y = rateY * (float)Host.ClientStageH;
            }
            if (rateX > 1f || rateY > 1f) realign = true;
            SetPosition(x, y);
        }
        LoadSettings(s);
        placedFromFile = true;
    }

    /// <summary>FUN_1422dc450 (uiswfentity.cpp:343): the layout, then m_60 cleared.</summary>
    public void LayoutAndClear(double stageW, double stageH)
    {
        Layout(stageW, stageH);
        realign = false;
    }

    /// <summary>
    /// UIDialog::vfunc_18 (uidialog.cpp:734), the window was resized: an open dialog with a drag bar keeps its position's
    /// ratio to the stage, from the stage size cached before the resize (GFxManager::FUN_1422b0840; ResizeMovies caches it
    /// before resizing each movie, gfxmanager.cpp:164) to the new one. An opening dialog first stops its open tween
    /// (FUN_141924510). Then UISwfEntity::vfunc_18: the layout (FUN_1422dc450).
    /// </summary>
    public void Resize(double oldW, double oldH, double newW, double newH)
    {
        if (IsDialog && HasDragBar && !IsClosed)
        {
            if (closeState == 0 && !Rt.IsNullish(Host.Get(Host.Entity, "m_openTween"))) Host.Invoke("stopOpenTweens");
            var (x, y) = Position();
            float rateX = x / (float)(int)oldW, rateY = y / (float)(int)oldH;
            SetPosition((float)(int)newW * rateX, (float)(int)newH * rateY);
        }
        LayoutAndClear(newW, newH);
    }

    /// <summary>UIDialog::vfunc_28 (uidialog.cpp:879): the dialog callbacks every UIDialog handles before its subclass.</summary>
    internal bool DialogCallback(string name)
    {
        switch (name.ToLowerInvariant())
        {
            case "onclosedialog": Close(); return true;           // vfunc_70 -> vfunc_67 (vfunc_71 is 0)
            case "onstartdrag": dragging = true; return true;     // vfunc_76
            case "onstopdrag": StopDrag(); return true;           // vfunc_77
            case "ontopmostlayer": return true;                   // vfunc_78: BringToFront; one movie per window here
            case "onendclose":
                if (closeState != 2) { closeState = 2; Unloaded?.Invoke(); }
                return true;
        }
        return false;
    }

    /// <summary>vfunc_77 (uidialog.cpp:794): a drag that ends marks the dialog moved (m_74) and saves the file.</summary>
    void StopDrag()
    {
        if (!dragging) return;
        Host.Invoke("stopDrag");
        dragging = false;
        EditAlign = 1;
        SaveTransform();
    }

    /// <summary>
    /// vfunc_67 (uidialog.cpp:607): FUN_141924190 (an unfinished drag ends and saves, m_78 cleared), then FUN_141924330
    /// with the tween on (OnLoadComplete opens with the tween): stopTweens, the file saved while the dialog was open (m_68
    /// 0), the closing state and startClose; the movie's OnEndClose unloads it.
    /// </summary>
    public virtual void Close()
    {
        if (!IsDialog) return;
        if (!closed)
        {
            placedFromFile = false;
            StopDrag();
        }
        Host.Invoke("stopTweens");
        if (!closed) SaveTransform();
        closeState = 1;
        Host.Invoke("startClose");
        closed = true;
    }

    public virtual void OnLoadComplete()
    {
        if (!IsDialog) return;
        closed = false;
        closeState = 0;
        Host.SetVisible(Host.Entity, true);
        Host.Invoke("startOpen");
    }

    /// <summary>A call the subclass's vfunc_28 takes before UIDialog's dialog calls (UIGameOptionDialog's OnCloseDialog);
    /// true = handled.</summary>
    public virtual bool OnCallbackBeforeDialog(string name, object[] args) => false;

    /// <summary>ExternalInterface callback from the movie; true = handled.</summary>
    public virtual bool OnCallback(string name, object[] args) => false;

    /// <summary>MS2DataModel.getUserData(id, name, args) (vfunc_45): the members of the returned object, or null.
    /// UIObject's (uiobject.cpp:117) answers "getTooltip" with <see cref="GetTooltip"/> (vfunc_49).</summary>
    public virtual IDictionary<string, object> GetUserData(string name, object[] args)
        => name == "getTooltip" ? GetTooltip(args) : null;

    /// <summary>vfunc_49 (UIObject's @0x14131edf0 is empty): the tooltip of a movie item whose tooltip text is
    /// "#callback" (TooltipManager.showTooltipMouseEvent passes the item's name), an <see cref="AsTooltip"/>; null = none.</summary>
    public virtual IDictionary<string, object> GetTooltip(object[] args) => null;

    /// <summary>The movie's ExternalInterface events other than its dialog calls (GFxMovie vfunc_4 of the movie's own
    /// class, e.g. GFxMovieTooltip's "initializeClass"); true = handled.</summary>
    public virtual bool OnMovieEvent(string ev, object[] args) => false;

    public virtual void OnStatus(StatusEvent e) { }

    /// <summary>The value a DataBinding slot name currently holds (null = empty); the model's item slots
    /// (<see cref="UiModel.ItemSlots"/>) by default.</summary>
    public virtual object GetBinding(string name)
        => Host?.Model is { } m && m.ItemSlots.TryGetValue(name, out var slot) ? slot.Value(Host, m, m.ItemSlotSources) : null;

    /// <summary>GameDataModel.getBagSlotBindings(bag): the slot binding names of a bag.</summary>
    public virtual IEnumerable<string> GetBagSlotBindings(string bag) => Array.Empty<string>();

    protected static string Thousands(long n) => n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
}
