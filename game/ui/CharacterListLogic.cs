using Godot;
using Ms2.Net.Packets;
using Ms2Godot.Ui.Contracts;

namespace Ms2Godot.Ui;

/// <summary>
/// The character select dialog's behaviour, ported by hand from the movie's AS3 (CharacterListDialog, CharacterList,
/// CharacterRenderer) against the generated contract. Each row is filled with the object the C++ dialog builds
/// (<see cref="LobbyCharacterData"/>, FUN_1418f42c0); what the movie tells the game goes through <see cref="UiBridge"/>.
/// </summary>
public sealed class CharacterListLogic
{
    const string Movie = UICharacterListDialogContract.Movie;
    public const int MaxRow = 8;                 // rows built here (the movie's CharacterList allows up to 15)
    public int MaxCharCount = 6;                 // slots the account may use; the rest show a lock

    sealed class Row
    {
        public MovieClipNode Clip;
        public CharacterEntry Entry;
        public LobbyCharacterRow Data;
        public bool Empty, Locked;
    }

    readonly UiMovie movie;
    readonly MovieClipNode root, list;
    readonly Vector2 listScale;
    readonly List<Row> rows = new();
    public int SelectedIndex { get; private set; } = -1;
    public ButtonWidget DeleteButton, ServerListButton, StartButton;

    /// <summary>LobbyDialog.setMouseState: the dialog takes no clicks while false (a start or a lobby change is under way).</summary>
    public bool MouseEnabled = true;

    public CharacterListLogic(UiMovie movie, MovieClipNode root)
    {
        this.movie = movie; this.root = root;
        list = root.FindClip(UICharacterListDialogContract.Instances.M_content_m_charlist) ?? throw new InvalidOperationException("character list: missing m_charlist");
        // the list component takes its size from the stretched frame it is placed with, then runs unscaled (CLIK UIComponent)
        listScale = list.Scale; list.Scale = Vector2.One;
        DeleteButton = root.FindClip(UICharacterListDialogContract.Instances.M_content_m_delete)?.Widget as ButtonWidget;
        ServerListButton = root.FindClip(UICharacterListDialogContract.Instances.M_content_m_serverlist)?.Widget as ButtonWidget;
        StartButton = root.FindClip("m_content.m_start")?.Widget as ButtonWidget;   // CharacterListDialog: m_start = m_content.m_start
        if (DeleteButton != null) DeleteButton.Click += _ => UiBridge.Raise(Movie, UICharacterListDialogContract.ToHost.OnDeletePC);
        if (ServerListButton != null) ServerListButton.Click += _ => UiBridge.Raise(Movie, UICharacterListDialogContract.ToHost.OnSceneLobbyServer);
        if (StartButton != null) StartButton.Click += _ => ClickStart();             // onClickStart
    }

    int RendererId(string cls) => movie.Scope.Symbols.Where(kv => (string)kv.Value["class"] == cls).Select(kv => int.Parse(kv.Key)).FirstOrDefault();

    /// <summary>
    /// CharacterListDialog.buildCharacterList(characters, selectedIndex, maxCharCount), with each character's object from
    /// <paramref name="data"/>; <paramref name="image"/> loads a profile picture path (Common.loadProfileImage).
    /// </summary>
    public void BuildCharacterList(IReadOnlyList<CharacterEntry> characters, int selected, int maxCharCount, LobbyCharacterData data, Func<string, Texture2D> image)
    {
        foreach (var r in rows) { list.RemoveChild(r.Clip); r.Clip.QueueFree(); }
        rows.Clear();
        MaxCharCount = maxCharCount;
        var s = movie.Scope; var assets = movie.Assets;
        // the movie's list scales its 100 x 100 frame; renderers are placed in the list's own units
        for (int i = 0; i < MaxRow; i++)
        {
            bool has = i < characters.Count;
            bool locked = !has && i >= maxCharCount;
            var clip = UiBuilder.BuildSprite(movie, s, RendererId(has ? "charlist_renderer_char" : "charlist_renderer_empty"), list.sx / listScale.X, list.sy / listScale.Y);
            clip.Position = new Vector2(0, i * 93f);
            list.AddChild(clip);
            clip.SetNamedVisible("m_delete", false);          // the delete-pending state is shown by requestDelete
            clip.SetNamedVisible("m_burningLevelupEventIcon", false);
            var row = new Row { Clip = clip, Entry = has ? characters[i] : null, Empty = !has, Locked = locked };
            if (has)
            {
                // CharacterRenderer.setData
                var d = row.Data = data.Row(characters[i]);
                clip.SetNamedText("m_name", d.CharName);
                clip.SetNamedText("m_date", d.CreatedDate);
                clip.SetNamedText("m_pos", d.LastPos);
                clip.SetNamedText("m_levelJob", d.LevelJob);
                LoadProfile(clip, image(d.ImgPath));
            }
            else
            {
                clip.SetNamedText("textField", locked ? "" : assets.Str("$s_charselect_create_newchar"));
                clip.SetNamedVisible("m_lockIcon", locked);
            }
            rows.Add(row);
        }
        Select(Math.Clamp(selected, 0, rows.Count - 1), false);
        UpdateScrollBar();
    }

    /// <summary>
    /// m_scroll (export_scroll : ScrollBarEx, scrollTarget m_charlist). CoreList.updateScrollBar sets
    /// setScrollProperties(totalRows, 0, rows - totalRows) at scroll position 0; TileCacheList.calculateRendererTotal gives
    /// totalRows = floor((743 + 1) / (92 + 1)) = 8 (list height 100 x 7.43, rowHeight 92, rendererSpacing.y 1).
    /// ScrollBar.updateThumbPosition hides the thumb when (position - min) / (max - min) is NaN or max is not positive, and
    /// enables the arrows and the track only with the thumb shown and room to move that way. The list here holds MaxRow rows,
    /// all in view, so the thumb is hidden and both arrows are disabled.
    /// </summary>
    void UpdateScrollBar()
    {
        if (root.FindClip("m_content.m_scroll") is not { } bar) return;
        const int TotalRows = 8;
        double min = 0, max = rows.Count - TotalRows, pos = 0;
        double frac = (pos - min) / (max - min);
        bool thumbOn = !(double.IsNaN(frac) || max <= 0 || double.IsInfinity(max));
        void State(string name, bool on) { if (bar.Direct(name)?.Node is MovieClipNode b && b.HasLabel("up")) b.GotoLabel(on ? "up" : "disabled"); }
        State("upArrow", thumbOn && pos > min);
        State("downArrow", thumbOn && pos < max);
        State("track", thumbOn);
        bar.SetNamedVisible("thumb", thumbOn);
        bar.GotoFrame(bar.Frame);
    }

    /// <summary>
    /// CharacterRenderer.updateProfile / onLoadCompleteProfileImage: the picture goes into m_profile, square, with the side
    /// m_profileBg.width - (m_profile.x - m_profileBg.x) * 2.
    /// </summary>
    static void LoadProfile(MovieClipNode clip, Texture2D tex)
    {
        var bg = clip.Direct("m_profileBg"); var pr = clip.Direct("m_profile");
        if (tex == null || bg?.Node is not MovieClipNode bgClip || pr?.Node is not MovieClipNode prClip) return;
        float bgWidth = (bgClip.ContentRect()?.Size.X ?? bgClip.Bounds.Size.X) * bg.Base.Scale.X;
        float side = bgWidth - (pr.Base.Origin.X - bg.Base.Origin.X) * 2;
        if (side <= 0) return;
        foreach (var old in prClip.GetChildren().OfType<Sprite2D>()) old.QueueFree();
        prClip.AddChild(new Sprite2D
        {
            Texture = tex, Centered = false, TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            Scale = new Vector2(side / tex.GetWidth(), side / tex.GetHeight()) / pr.Base.Scale,
        });
    }

    /// <summary>
    /// The list's selection: each renderer to its (selected_)up state (CharacterRenderer.updateAfterStateChange: the job
    /// icon of the state and m_profileBg default / selected), then CharacterListDialog.onChangeCharacter: updateStartButton
    /// and OnSelectCharacter with the row's charSN.
    /// </summary>
    public void Select(int index, bool raise)
    {
        if (index < 0 || index >= rows.Count) return;
        SelectedIndex = index;
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            bool sel = i == index;
            r.Clip.GotoLabel(sel ? "selected_up" : "up");
            if (r.Data == null) continue;
            // setJobIcon: m_jobicon or m_jobicon_sel to the frame "_" + jobcode
            foreach (var name in new[] { "m_jobicon", "m_jobicon_sel" })
                if (r.Clip.Direct(name)?.Node is MovieClipNode icon) icon.GotoLabel("_" + r.Data.JobCode);
            if (r.Clip.Direct("m_profileBg")?.Node is MovieClipNode pbg) pbg.GotoLabel(sel ? "selected" : "default");
        }
        UpdateStartButton(index);
        if (raise) UiBridge.Raise(Movie, UICharacterListDialogContract.ToHost.OnSelectCharacter, rows[index].Data?.CharSN ?? "");
    }

    /// <summary>
    /// CharacterListDialog.updateStartButton: "$s_charselect_start_game" on a character, "$s_charselect_create_char" on an
    /// empty slot; disabled on a locked empty slot.
    /// </summary>
    void UpdateStartButton(int index)
    {
        if (StartButton == null || index < 0 || index >= rows.Count) return;
        var r = rows[index];
        StartButton.Enabled = !(r.Empty && r.Locked);
        StartButton.Set("label", r.Empty ? "$s_charselect_create_char" : "$s_charselect_start_game");
    }

    /// <summary>Row under a canvas point; each row is 402 wide, 92 tall in the list's units.</summary>
    public int RowAt(Vector2 p)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            var xf = rows[i].Clip.GetGlobalTransform();
            var a = xf * new Vector2(0, 0); var b = xf * new Vector2(402, 92);
            if (new Rect2(a, b - a).Abs().HasPoint(p)) return i;
        }
        return -1;
    }

    public void OnMouse(InputEventMouseButton mb)
    {
        if (!MouseEnabled || !mb.Pressed || mb.ButtonIndex != MouseButton.Left) return;
        int i = RowAt(mb.Position);
        if (i < 0) return;
        if (mb.DoubleClick) { ClickStart(i); return; }     // onDoubleClickCharacter
        if (i != SelectedIndex && !rows[i].Locked) Select(i, true);
    }

    /// <summary>
    /// CharacterListDialog.clickStart on the selected row (the start button, Enter): with the mouse state on, a character
    /// starts the game (the mouse state goes off), an empty slot that is not locked begins character creation.
    /// </summary>
    public void ClickStart() => ClickStart(SelectedIndex);

    void ClickStart(int index)
    {
        if (index < 0 || index >= rows.Count || !MouseEnabled) return;
        var row = rows[index];
        if (!row.Empty)
        {
            MouseEnabled = false;
            UiBridge.Raise(Movie, UICharacterListDialogContract.ToHost.OnGameStart, row.Data.CharSN);
        }
        else if (!row.Locked) UiBridge.Raise(Movie, UICharacterListDialogContract.ToHost.OnCreatePC);
    }

    /// <summary>CharacterListDialog.onEnterKey: clickStart on the selected row.</summary>
    public void OnEnterKey() => ClickStart();
}
