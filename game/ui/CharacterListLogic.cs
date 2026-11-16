using Godot;
using Ms2.Net.Packets;
using Ms2Godot.Ui.Contracts;

namespace Ms2Godot.Ui;

/// <summary>
/// The character select dialog's behaviour, ported by hand from the movie's AS3 (CharacterListDialog, CharacterList,
/// CharacterRenderer) against the generated contract. The list is filled from Ms2.Net's <see cref="CharacterEntry"/>;
/// what the movie tells the game goes through <see cref="UiBridge"/>.
/// </summary>
public sealed class CharacterListLogic
{
    const string Movie = UICharacterListDialogContract.Movie;
    public const int MaxRow = 8;                 // rows built here (the movie's CharacterList allows up to 15)
    public int MaxCharCount = 6;                 // slots the account may use; the rest show a lock

    readonly UiMovie movie;
    readonly MovieClipNode root, list;
    readonly Vector2 listScale;
    readonly List<(MovieClipNode Clip, CharacterEntry Entry, bool Empty)> rows = new();
    public int SelectedIndex { get; private set; } = -1;
    public ButtonWidget DeleteButton, ServerListButton;

    public CharacterListLogic(UiMovie movie, MovieClipNode root)
    {
        this.movie = movie; this.root = root;
        list = root.FindClip(UICharacterListDialogContract.Instances.M_content_m_charlist) ?? throw new InvalidOperationException("character list: missing m_charlist");
        // the list component takes its size from the stretched frame it is placed with, then runs unscaled (CLIK UIComponent)
        listScale = list.Scale; list.Scale = Vector2.One;
        DeleteButton = root.FindClip(UICharacterListDialogContract.Instances.M_content_m_delete)?.Widget as ButtonWidget;
        ServerListButton = root.FindClip(UICharacterListDialogContract.Instances.M_content_m_serverlist)?.Widget as ButtonWidget;
        if (DeleteButton != null) DeleteButton.Click += _ => UiBridge.Raise(Movie, UICharacterListDialogContract.ToHost.OnDeletePC);
        if (ServerListButton != null) ServerListButton.Click += _ => UiBridge.Raise(Movie, UICharacterListDialogContract.ToHost.OnSceneLobbyServer);
    }

    int RendererId(string cls) => movie.Scope.Symbols.Where(kv => (string)kv.Value["class"] == cls).Select(kv => int.Parse(kv.Key)).FirstOrDefault();

    /// <summary>buildCharacterList(characters, selectedIndex, maxCharCount)</summary>
    public void BuildCharacterList(IReadOnlyList<CharacterEntry> characters, int selected, int maxCharCount)
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
            if (has)
            {
                var u = characters[i].User;
                clip.SetNamedText("m_name", u.Name);
                clip.SetNamedText("m_levelJob", $"Lv. {u.Level}  {StubCharacterList.JobNames.GetValueOrDefault(u.Job, "Beginner")}");
                clip.SetNamedText("m_pos", $"Map {u.MapA}");
                clip.SetNamedText("m_date", DateTimeOffset.FromUnixTimeSeconds(u.CreationTime).ToString("yyyy-MM-dd"));
            }
            else
            {
                clip.SetNamedText("textField", locked ? "" : assets.Str("$s_charselect_create_newchar"));
                clip.SetNamedVisible("m_lockIcon", locked);
            }
            rows.Add((clip, has ? characters[i] : null, !has));
        }
        Select(Math.Clamp(selected, 0, Math.Max(0, characters.Count - 1)), false);
    }

    public void Select(int index, bool raise)
    {
        if (index < 0 || index >= rows.Count) return;
        SelectedIndex = index;
        for (int i = 0; i < rows.Count; i++) rows[i].Clip.GotoLabel(i == index ? "selected_up" : "up");
        if (raise) UiBridge.Raise(Movie, UICharacterListDialogContract.ToHost.OnSelectCharacter, index);
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
        if (!mb.Pressed || mb.ButtonIndex != MouseButton.Left) return;
        int i = RowAt(mb.Position);
        if (i < 0) return;
        if (i != SelectedIndex) { if (!rows[i].Clip.IsLockedRow()) Select(i, true); }
        else ClickStart();
    }

    /// <summary>Enter key / second click on the selected row: start the game with a character, or begin character creation on an empty slot.</summary>
    public void ClickStart()
    {
        if (SelectedIndex < 0) return;
        var row = rows[SelectedIndex];
        if (row.Empty) UiBridge.Raise(Movie, UICharacterListDialogContract.ToHost.OnCreatePC);
        else UiBridge.Raise(Movie, UICharacterListDialogContract.ToHost.OnGameStart, row.Entry.User.CharacterId.ToString());
    }

    public void OnEnterKey() => ClickStart();
}

static class ClipExt
{
    /// <summary>A locked empty slot: its lock icon is shown.</summary>
    public static bool IsLockedRow(this MovieClipNode clip) => clip.Children.Any(c => c.Name == "m_lockIcon" && c.Last == int.MaxValue);
}
