using System.Numerics;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIBootyPopup (uibootypopup.cpp, UI 0x4ebb, "BootyPopup", movie uibootypopup on layer 7): the box over a function cube
/// whose object has firstAdditionalPopup, naming the cube's item and the buff its object gives. CFunctionCubeObject::vfunc_2
/// (functioncubeobject.cpp:574-582) makes it with the cube: its point is the cube's position 150 up (FUN_141031660), the
/// cube's entity and item (SetEntityId, SetItemId), then Show; the cube's destructor closes it (:437-439).
/// <para>Show (@0x1410315c0) sets m_78 and m_74; Close (@0x1410315f0) takes the entity away and clears both. Each frame
/// (vfunc_15 @0x1410317a0, with a local player): a set m_78 hides the movie and clears (vfunc_52 @0x1410316f0); then a shown
/// popup places itself (UpdateScreenPosition @0x141031890), else the movie is hidden.</para>
/// </summary>
public sealed class BootyPopup
{
    /// <summary>m_68: the cube's position, 150 up.</summary>
    public Vector3 Point { get; init; }
    /// <summary>m_88: the placed cube's item.</summary>
    public int ItemId { get; init; }
    /// <summary>m_74 (IsShown).</summary>
    public bool Shown { get; private set; }
    /// <summary>m_78: the movie is hidden at the next frame.</summary>
    public bool HidePending { get; private set; }
    /// <summary>m_7c: the text is up (FUN_141031a60's state).</summary>
    public bool TextShown { get; private set; }
    /// <summary>The movie's visibility (vfunc_8).</summary>
    public bool Visible { get; private set; }
    /// <summary>The movie's position (SetPositionFloat of the root), set by <see cref="Frame"/>.</summary>
    public Vector2 Position { get; private set; }
    public bool Closed { get; private set; }

    /// <summary>The movie's root calls: setText(name, description), then setShow(bool).</summary>
    public Action<string, string> SetText;
    public Action<bool> SetShow;

    public void Show() { HidePending = true; Shown = true; }

    public void Close() { Closed = true; HidePending = false; Shown = false; }

    /// <summary>
    /// FUN_141031a60 (@0x141031a60): nothing when the text state is already <paramref name="show"/>; else it is set, a show
    /// first pushes setText with <paramref name="text"/> (the item's name and the buff's description), then setShow.
    /// </summary>
    public void SetTextShown(bool show, Func<int, (string Name, string Description)> text)
    {
        if (TextShown == show) return;
        TextShown = show;
        if (show)
        {
            var (name, desc) = text(ItemId);
            SetText?.Invoke(name, desc);
        }
        SetShow?.Invoke(show);
    }

    /// <summary>
    /// vfunc_15 and UpdateScreenPosition: with a local player, a pending hide hides the movie; a shown popup with its text
    /// up is placed where <paramref name="project"/> puts its point (CameraMgr FUN_140af0700) and shown, times the movie scale,
    /// centred (the width rounded up, halved in integers) and 15 above its height; a point off the screen hides it. A shown
    /// popup without its text keeps its visibility; one not shown is hidden.
    /// </summary>
    public void Frame(bool hasLocalPlayer, Func<Vector3, Vector2?> project, float movieScale, int width, int height)
    {
        if (Closed || !hasLocalPlayer) return;
        if (HidePending) { Visible = false; HidePending = false; }
        if (!Shown) { Visible = false; return; }
        if (!TextShown) return;
        if (project(Point) is not { } s) { Visible = false; return; }
        Visible = true;
        s *= movieScale;
        Position = new Vector2(s.X - (float)(width / 2), s.Y - height - 15f);
    }

    /// <summary>
    /// FUN_141031a60's text: the item's name (CStringTable FUN_141811980(1, item)); then, when the item has data, its install
    /// object's (GetObjectData(install.objCode), CItemData +0x1e0) first additional effect's tooltipDescription at its level
    /// (FUN_141812290) and, when the effect's level has data (SkillDataTableCli FUN_140aafef0), a line break and
    /// s_bootypopup_buff_duration (common string 0x9a8) with the duration's whole minutes (FUN_141b706a0).
    /// </summary>
    public static (string Name, string Description) Text(GameTables t, int itemId)
    {
        string name = t.ItemName(itemId);
        string desc = "";
        var item = t.Items.Get(itemId);
        if (item != null && t.ObjectDataOf(item.InstallObjCode) is { } obj)
        {
            uint id = (uint)obj.Cube.FirstEffect.EffectId;
            ushort level = (ushort)obj.Cube.FirstEffect.Level;
            desc = t.AdditionalDescription(id, level, "tooltipDescription");
            if (t.Additional.GetLevel(id, level) is { } lv)
                desc += "\n" + ClientFormat.Format(t.Common(BuffDurationText), (int)(lv.DurationTick / 1000 / 60));
        }
        return (name, desc);
    }

    /// <summary>GetStringCommon(0x9a8): s_bootypopup_buff_duration.</summary>
    public const int BuffDurationText = 0x9a8;
}

/// <summary>
/// The function cubes' booty popups and CubeMapInformationService::FUN_1410c8a90 (@0x1410c8a90,
/// cubemapinformationservice_3.cpp:3491-3520), which runs each frame: the nearest usable cube within FunctionCubeDistance of
/// the pc (FUN_141230f60) whose object has the popup flag shows its text (f480 the cube, f490 shown, f494 the tick). The same
/// cube keeps it until BootyPopupDuration ms have passed, then hides it and shows it again only after the pc left it; another
/// cube hides the last one and shows its own; no such cube hides the last one.
/// </summary>
public sealed class BootyPopups : IDisposable
{
    readonly FunctionCubes cubes;
    readonly Dictionary<string, BootyPopup> popups = new(StringComparer.Ordinal);
    FunctionCubes.Cube current;
    bool shown;
    uint shownAt;

    /// <summary>The ClientConstants FunctionCubeDistance (+0xf80) and BootyPopupDuration (+0x5078).</summary>
    public float Distance;
    public int Duration;

    /// <summary>The text of an item (<see cref="BootyPopup.Text"/>).</summary>
    public Func<int, (string Name, string Description)> TextOf = id => ("", "");

    /// <summary>A popup was made or closed; the host makes or drops its movie.</summary>
    public Action<FunctionCubes.Cube, BootyPopup> Opened, Closed;

    public BootyPopups(FunctionCubes cubes)
    {
        this.cubes = cubes;
        cubes.Added += OnAdded;
        cubes.Removed += OnRemoved;
        foreach (var c in cubes.All) OnAdded(c);
    }

    /// <summary>Stops following the cubes (the field is left).</summary>
    public void Dispose()
    {
        cubes.Added -= OnAdded;
        cubes.Removed -= OnRemoved;
    }

    public int Count => popups.Count;
    public BootyPopup Of(string cubeId) => cubeId != null && popups.TryGetValue(cubeId, out var p) ? p : null;
    public IEnumerable<KeyValuePair<string, BootyPopup>> All => popups;

    void OnAdded(FunctionCubes.Cube c)
    {
        if (!c.HasBootyPopup) return;
        var p = new BootyPopup { Point = c.Position + new Vector3(0, 0, 150f), ItemId = c.ItemId };
        popups[c.Id] = p;
        Opened?.Invoke(c, p);
        p.Show();
    }

    void OnRemoved(FunctionCubes.Cube c)
    {
        if (!popups.Remove(c.Id, out var p)) return;
        p.Close();
        Closed?.Invoke(c, p);
    }

    void SetShown(FunctionCubes.Cube c, bool on)
    {
        if (c != null && popups.TryGetValue(c.Id, out var p) && !p.Closed) p.SetTextShown(on, TextOf);
    }

    /// <summary>FUN_1410c8a90 at <paramref name="now"/> (GetTickCountMs2) for the pc at <paramref name="pc"/> (null: no local player).</summary>
    public void Tick(Vector3? pc, uint now)
    {
        if (pc is not { } at) return;
        var slot = cubes.NearestUsable(at, Distance);
        if (slot != null && slot.HasBootyPopup)
        {
            if (current != null && current == slot)
            {
                if (shown)
                {
                    if (unchecked(shownAt + (uint)Duration) < now) SetShown(current, false);
                }
                else
                {
                    SetShown(current, true);
                    shownAt = now;
                    shown = true;
                }
            }
            else
            {
                if (current != null) SetShown(current, false);
                current = slot;
                SetShown(current, true);
                shownAt = now;
                shown = true;
            }
        }
        else
        {
            if (current != null) SetShown(current, false);
            shown = false;
        }
    }
}
