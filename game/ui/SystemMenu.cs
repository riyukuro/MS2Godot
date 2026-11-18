using Godot;
using Ms2Godot.Ui.Contracts;

namespace Ms2Godot.Ui;

/// <summary>
/// The system menu, UISystemMenuDialog (UI id 0x4e21, uisystemmenudialog.gfx): the C++ dialog (uisystemmenudialog.cpp) and
/// the movie's AS3 (scaleform.ms2.controls.dialog.SystemMenuDialog) ported by hand over
/// <see cref="UISystemMenuDialogContract"/>. Esc toggles it (FUN_1418fcb70, see <see cref="HudScene.Escape"/>); opening it
/// closes the other dialogs (vfunc_65, cpp:147, CloseAllDialogs; the menu itself has flag 1 and stays). It does not pause the
/// field: nothing in the dialog or UIDialog does.
/// </summary>
public sealed partial class SystemMenu : Node2D
{
    /// <summary>OpenGameOptionDialog(n) (cpp:445-452): the option dialog's page; the menu closes after it.</summary>
    public static event Action<int> OpenGameOption;
    /// <summary>OnSelectCharacter (cpp:461-464): GetNetworkService()->SendRequestQuit(), back to character select. The menu stays open.</summary>
    public static event Action SelectCharacter;
    /// <summary>OnGameExit, the shared FUN_1418fc760 (uichatballoon.cpp:463-467): message box 0x288 (s_msg_game_over), type 0x17, tag s_gameExit.</summary>
    public static event Action GameExit;

    const string Movie = UISystemMenuDialogContract.Movie;
    readonly UiMovie movie;
    readonly MovieClipNode root, body;
    readonly UiWidgets.MouseRouter mouse;
    readonly ButtonWidget exitRoom, selectChannel, graphic, systemOption, selectCharacter, exitGame, returnToGame;
    readonly DropdownWidget channel;
    readonly Dictionary<ButtonWidget, bool> shown = new();
    readonly float u;
    readonly Vector2 stage;
    float bgHeight;
    /// <summary>m_198, SetRedCircle (cpp:426): the option entry's red circle, false unless the personal-info agreement asks (gameoptionstruct.cpp:3511-3514; not ported, so false as in the ctor).</summary>
    public bool RedCircle;

    /// <summary>The open state: UIDialog m_c0 (open while shown or tweening open; closing until OnEndClose).</summary>
    public bool IsOpen { get; private set; }

    /// <param name="mapType">The field data's map type (CFieldDataCli::m_nMapType), -1 when the field has no field data row.</param>
    /// <param name="channelLabel">GetStringCommon(0x579) (s_word_ch).</param>
    /// <param name="channelGroupName">The field's channel group name (string table 23); "" for none.</param>
    /// <param name="leaveText">GetLeaveButtonText(mapType), null when not resolved.</param>
    public SystemMenu(UiAssets assets, float u, Vector2 stage, int mapType, string leaveText, string channelLabel, string channelGroupName)
    {
        this.u = u; this.stage = stage;
        Visible = false;
        movie = new UiMovie(assets, Movie);
        root = movie.BuildRoot(u);
        root.Scale = new Vector2(u, u);
        AddChild(root);
        body = root.Children.Select(c => c.Node).OfType<MovieClipNode>().First();
        mouse = new UiWidgets.MouseRouter(root);

        ButtonWidget B(string name) => (ButtonWidget)(body.FindClip(name) ?? throw new InvalidOperationException("system menu: missing " + name)).Widget;
        exitRoom = B(UISystemMenuDialogContract.Instances.M_exitRoom);
        selectChannel = B(UISystemMenuDialogContract.Instances.M_selectChannel);
        graphic = B(UISystemMenuDialogContract.Instances.M_graphic);
        systemOption = B(UISystemMenuDialogContract.Instances.M_systemOption);
        selectCharacter = B(UISystemMenuDialogContract.Instances.M_selectCharacter);
        exitGame = B(UISystemMenuDialogContract.Instances.M_exitGame);
        returnToGame = B(UISystemMenuDialogContract.Instances.M_returnToGame);
        channel = (DropdownWidget)body.FindClip("m_channel").Widget;
        foreach (var b in new[] { exitRoom, selectChannel, graphic, systemOption, selectCharacter, exitGame, returnToGame })
        {
            shown[b] = true;
            b.Node.SetNamedVisible("m_redCircle", false);       // SystemMenuButton.configUI
        }

        // frame 1 (__setProp_*): the entries' labels
        exitRoom.Set("label", "$s_systemmenu_channel");
        selectChannel.Set("label", "$s_systemmenu_channel");
        graphic.Set("label", "$s_systemmenu_graphic");
        systemOption.Set("label", "$s_systemmenu_option");
        selectCharacter.Set("label", "$s_systemmenu_character");
        exitGame.Set("label", "$s_systemmenu_exit");
        returnToGame.Set("label", "$s_systemmenu_resume");
        // __setProp_m_channel_SystemMenuDialog_channel_0: menuMargin 1, menuOffset top 1, menuRowCount 5
        channel.MenuMargin = 1; channel.MenuOffset = new Vector4(0, 1, 0, 0); channel.MenuRowCount = 5;
        channel.Dropdown = "export_scrollinglist_black"; channel.ItemRenderer = "export_renderer_dropdown";

        // the SystemMenuDialog ctor: CLICK listeners, m_channel hidden; configUI hides m_exitRoom
        selectChannel.Click += _ => SetShown(channel, !channelShown);                              // onSelectChannel
        systemOption.Click += _ => { HideChannel(); ToHost(UISystemMenuDialogContract.ToHost.OnGameOptionDialog); };
        selectCharacter.Click += _ => { HideChannel(); ToHost(UISystemMenuDialogContract.ToHost.OnSelectCharacter); };
        exitGame.Click += _ => { HideChannel(); ToHost(UISystemMenuDialogContract.ToHost.OnGameExit); ToHost(UISystemMenuDialogContract.ToHost.OnCloseDialog); };
        graphic.Click += _ => { HideChannel(); ToHost(UISystemMenuDialogContract.ToHost.OnGraphic); };
        returnToGame.Click += _ => { HideChannel(); ToHost(UISystemMenuDialogContract.ToHost.OnCloseDialog); };
        channel.IndexChange += (_, i) => { if (i >= 0) ToHost(UISystemMenuDialogContract.ToHost.OnChangeChannel, (int)channel.Items[i].Data); };
        HideChannel();
        SetShown(exitRoom, false);

        OnLoadInit(mapType, leaveText);
        Notify(channelLabel, channelGroupName);
    }

    bool channelShown = true;
    void HideChannel() => SetShown(channel, false);

    void SetShown(Widget w, bool on)
    {
        if (w == channel) { channelShown = on; if (!on) channel.Close(); }
        else if (w is ButtonWidget b) shown[b] = on;
        var c = body.Children.FirstOrDefault(x => x.Node == w.Node);
        if (c != null) { c.Hidden = !on; body.GotoFrame(body.Frame); }
    }

    /// <summary>
    /// OnLoadInit (cpp:165-193): the leave entry shows, with GetLeaveButtonText's text, only when the field has field data,
    /// FUN_1403751b0 holds (map type 3, 5, 7, 8, 9, 13, 14, 17 or 90; fielddata) and it is a home (map type 3) or
    /// FUN_1418fd280() is 0 (not in the own house: no house state offline). Then arrangeConrols and the red circle.
    /// </summary>
    void OnLoadInit(int mapType, string leaveText)
    {
        bool leave = mapType is 3 or 5 or 7 or 8 or 9 or 13 or 14 or 17 or 90;
        if (leave && leaveText != null)
        {
            exitRoom.Node.SetText(leaveText);          // FUN_1422cdfb0: the button's label
            exitRoom.Click += _ => GD.Print($"system menu: leave entry (map type {mapType}): the leave box and the field move need the server, not ported");
            SetShown(exitRoom, true);
        }
        else if (leave) GD.PushWarning($"system menu: map type {mapType}: the leave entry's text (a dungeon name) is not resolved, entry left hidden");
        ArrangeConrols();
        systemOption.Node.SetNamedVisible("m_redCircle", RedCircle);      // setSystemOptionNotifyRedCircle(m_198)
    }

    /// <summary>
    /// SystemMenuDialog.arrangeConrols: from y 56 each shown entry is docked below the last (dockTopPos: y = pos + gap, pos =
    /// y + height), the resume entry 10 lower; the channel list sits 42 below the channel entry and the frame (m_bg) ends 12
    /// under the last entry.
    /// </summary>
    void ArrangeConrols()
    {
        float y = 56;
        y = Dock(y, exitRoom, 0);
        y = Dock(y, selectChannel, 0);
        y = Dock(y, graphic, 0);
        y = Dock(y, systemOption, 0);
        y = Dock(y, selectCharacter, 0);
        y = Dock(y, exitGame, 0);
        y = Dock(y, returnToGame, 10);
        var sel = body.Children.First(c => c.Node == selectChannel.Node);
        body.SetChildY(body.Children.First(c => c.Node == channel.Node), sel.Base.Origin.Y + 42);
        bgHeight = y + 12;
        body.ResizeNine(body.Direct("m_bg"), 282, bgHeight);
    }

    float Dock(float pos, ButtonWidget b, float gap)
    {
        if (!shown[b]) return pos;
        var c = body.Children.First(x => x.Node == b.Node);
        float y = (int)(pos + gap);                // DisplayObject.y keeps whole twips; the gaps here are whole pixels
        body.SetChildY(c, y);
        return y + Height(b);
    }

    /// <summary>A CLIK button's height: UIComponent.initialize takes the first frame's height (_height = super.height).</summary>
    static float Height(ButtonWidget b)
    {
        int f = b.Node.Frame;
        b.Node.GotoFrame(0);
        float h = b.Node.ContentRect()?.Size.Y ?? 0;
        b.Node.GotoFrame(f);
        b.Refresh();
        return h;
    }

    /// <summary>
    /// Notify (cpp:294-322) and SystemMenuDialog.buildChannelList: the server's channels of the current server
    /// (UIServerListDialog::BuildChannelList, uiserverlistdialog.cpp:744-772: rows {id, label = labelDropdown = "{0} {1}" of
    /// s_word_ch and the id}), the current channel selected; each row's text is the prefix (the channel group's name and a
    /// space, when it has one) plus " " plus labelDropdown. Offline there is one channel, 1, the current one.
    /// </summary>
    void Notify(string channelLabel, string groupName)
    {
        string prefix = string.IsNullOrEmpty(groupName) ? "" : groupName + " ";
        int current = 1;
        var rows = new List<(string, object)> { (prefix + " " + $"{channelLabel} {current}", current) };
        int selected = 0;
        channel.MenuRowCount = Math.Min(10, rows.Count);
        channel.SetData(rows, selected);
    }

    void ToHost(string name, params object[] args)
    {
        UiBridge.Raise(Movie, name, args);
        Host(name, args);
    }

    /// <summary>UISystemMenuDialog::vfunc_28 (cpp:238-272), with UIDialog::vfunc_28 for OnCloseDialog and FUN_1418fc760 for OnGameExit.</summary>
    void Host(string name, object[] args)
    {
        switch (name)
        {
            case UISystemMenuDialogContract.ToHost.OnGameOptionDialog: OpenGameOptionDialog(1); break;     // cpp:245
            case UISystemMenuDialogContract.ToHost.OnGraphic: OpenGameOptionDialog(6); break;                // cpp:255
            case UISystemMenuDialogContract.ToHost.OnSelectCharacter: SelectCharacter?.Invoke(); break;      // cpp:461-464
            case UISystemMenuDialogContract.ToHost.OnGameExit: GameExit?.Invoke(); break;                    // uichatballoon.cpp:463-467
            case UISystemMenuDialogContract.ToHost.OnCloseDialog: Close(); break;                            // vfunc_70 -> vfunc_67
            case UISystemMenuDialogContract.ToHost.OnChangeChannel:
                // WorldService::FUN_141bc80c0(ch) asks the server; offline there is no other channel, so the current one stays
                // selected (SelectCurrentChannel)
                channel.Select(0, false);
                break;
        }
    }

    void OpenGameOptionDialog(int page)
    {
        OpenGameOption?.Invoke(page);
        Close();
    }

    // ---- open / close (UIDialog FUN_141924680 / vfunc_67 and OpenCloseTween) ----

    Tween tween;
    Vector2 placed;     // the aligned position (stage units, the dialog's registration point)

    public void Toggle() { if (IsOpen) Close(); else Open(); }

    /// <summary>
    /// Opens with OpenCloseTween.startOpenAni: from 25 lower at alpha 0, 160 ms to alpha 1 and 7 above the place, then 80 ms
    /// to the place, both Regular.easeOut. Placed by Dialog.alignment (Align ALIGN_CENTER, 0, 0) and floored
    /// (SystemMenuDialog.alignment).
    /// </summary>
    public void Open(bool animate = true)
    {
        if (IsOpen) return;
        IsOpen = true;
        Visible = true;
        var size = new Vector2(282, bgHeight);
        var reg = UiLayout.Align(0, stage, size, 0, 0);
        placed = new Vector2(Mathf.Floor(reg.X), Mathf.Floor(reg.Y));
        tween?.Kill();
        if (!animate) { Place(0); root.Modulate = Colors.White; return; }
        Place(25); root.Modulate = new Color(1, 1, 1, 0);
        tween = CreateTween();
        tween.TweenMethod(Callable.From<float>(Place), 25f, -7f, 0.16).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(root, "modulate:a", 1f, 0.16).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.TweenMethod(Callable.From<float>(Place), -7f, 0f, 0.08).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
    }

    /// <summary>
    /// vfunc_67 with the tween on (m_c4): OpenCloseTween.startCloseAni, 100 ms to alpha 0 and 25 lower (Regular.easeOut), then
    /// OnEndClose unloads it. A reopen is centred again (m_d8 cleared, cpp:157).
    /// </summary>
    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        HideChannel();
        tween?.Kill();
        tween = CreateTween();
        tween.TweenMethod(Callable.From<float>(Place), 0f, 25f, 0.1).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(root, "modulate:a", 0f, 0.1).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.TweenCallback(Callable.From(() => { if (!IsOpen) Visible = false; }));        // OnEndClose
    }

    /// <summary>A scripted click on an entry (hover, press, release), for proof runs.</summary>
    public void ClickEntry(string name)
    {
        if (body.FindClip(name)?.Widget is not ButtonWidget b) return;
        b.MouseOver(true); b.MouseDown(true); b.MouseDown(false); b.MouseOver(false);
    }

    void Place(float dy) => root.Position = (placed + new Vector2(0, dy) - body.Position) * u;

    public override void _Input(InputEvent e)
    {
        if (!Visible || !IsOpen) return;
        if (e is not (InputEventMouseMotion or InputEventMouseButton)) return;
        mouse.Handle(e);
        // the movie takes a click on itself (Scaleform hit test), so the field under it does not get it
        if (e is InputEventMouseButton mb)
        {
            var local = root.GetGlobalTransform().AffineInverse() * mb.Position;
            if (body.HitTest(body.Transform.AffineInverse() * local) || channel.IsOpen) GetViewport().SetInputAsHandled();
        }
    }
}
