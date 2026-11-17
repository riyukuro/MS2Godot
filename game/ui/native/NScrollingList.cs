using Godot;

namespace Ms2Godot.Ui.Native;

/// <summary>
/// CLIK ScrollingList (CoreList + ScrollingList): a nine-patch background, a fixed set of item renderers
/// (rowCount = the rows that fit), a scroll position and a selected index. Renderers are ListItemRenderer buttons (their
/// selected state shows the selection); the wheel scrolls one row, Up/Down move the selection and scroll it into view,
/// a click selects and raises <see cref="ItemClickEventHandler"/>. An optional <see cref="NScrollBar"/> follows the list.
/// </summary>
[GlobalClass]
public partial class NScrollingList : Control
{
    [Export] public string Movie = "component_v1";
    [Export] public string Skin = "export_scrollinglist_black";
    [Export] public string RendererMovie = "component_v1";
    [Export] public string RendererSkin = "export_renderer_menu";
    [Export] public float Margin = 1;
    [Export] public NodePath ScrollBar = "";
    [Export] public string[] Items = System.Array.Empty<string>();

    [Signal] public delegate void ItemClickEventHandler(int index);
    [Signal] public delegate void ChangeEventHandler(int selectedIndex);

    readonly List<NButton> renderers = new();
    NSkin bg;
    NScrollBar bar;
    int scrollPosition, selectedIndex = -1;
    float rowHeight = 20;

    public int SelectedIndex { get => selectedIndex; set => Select(value); }
    public int ScrollPosition { get => scrollPosition; set => ScrollTo(value); }
    public int RowCount => renderers.Count;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All;
        if (NativeUi.Art == null) return;
        bg = new NSkin { Movie = Movie, Symbol = Skin, Size = Size };
        AddChild(bg);
        var r0 = NativeUi.Art.Bake(RendererMovie, RendererSkin, 0);
        if (r0 != null) rowHeight = r0.Size.Y;
        int rows = Math.Max(1, (int)((Size.Y - 2 * Margin) / rowHeight));
        for (int i = 0; i < rows; i++)
        {
            int slot = i;
            var r = new NButton { Movie = RendererMovie, Skin = RendererSkin, Position = new Vector2(Margin, Margin + i * rowHeight) };
            r.Size = new Vector2(Size.X - 2 * Margin - (ScrollBar.IsEmpty ? 0 : 0), rowHeight);
            r.Click += () => OnRendererClick(slot);
            AddChild(r);
            renderers.Add(r);
        }
        if (!ScrollBar.IsEmpty) bar = GetNodeOrNull<NScrollBar>(ScrollBar);
        if (bar != null) bar.Scroll += p => ScrollTo((int)Math.Round(p));
        Refresh();
    }

    public void SetItems(IEnumerable<string> items) { Items = items.ToArray(); scrollPosition = 0; selectedIndex = -1; Refresh(); }

    void OnRendererClick(int slot)
    {
        int index = scrollPosition + slot;
        if (index >= Items.Length) return;
        Select(index);
        EmitSignal(SignalName.ItemClick, index);
    }

    public void Select(int index)
    {
        index = Math.Clamp(index, -1, Items.Length - 1);
        if (index == selectedIndex) return;
        selectedIndex = index;
        // ScrollingList.scrollToIndex: bring the selection into view
        if (index >= 0 && index < scrollPosition) scrollPosition = index;
        else if (index >= scrollPosition + renderers.Count) scrollPosition = index - renderers.Count + 1;
        Refresh();
        EmitSignal(SignalName.Change, selectedIndex);
    }

    public void ScrollTo(int p)
    {
        p = Math.Clamp(p, 0, Math.Max(0, Items.Length - renderers.Count));
        if (p == scrollPosition) return;
        scrollPosition = p;
        Refresh();
    }

    /// <summary>refreshData / drawRenderers: renderer i shows item scrollPosition + i; unused renderers are hidden.</summary>
    void Refresh()
    {
        for (int i = 0; i < renderers.Count; i++)
        {
            int index = scrollPosition + i;
            var r = renderers[i];
            r.Visible = index < Items.Length;
            if (!r.Visible) continue;
            r.Text = Items[index];
            r.Selected = index == selectedIndex;
        }
        if (bar != null) { bar.SetScrollProperties(renderers.Count, 0, Math.Max(0, Items.Length - renderers.Count)); bar.SetPosition(scrollPosition, false); }
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true } mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp) { ScrollTo(scrollPosition - 1); AcceptEvent(); }
            else if (mb.ButtonIndex == MouseButton.WheelDown) { ScrollTo(scrollPosition + 1); AcceptEvent(); }
            else GrabFocus();
        }
        else if (e is InputEventKey { Pressed: true } k)
        {
            if (k.Keycode == Key.Up) { Select(Math.Max(0, selectedIndex - 1)); AcceptEvent(); }
            else if (k.Keycode == Key.Down) { Select(Math.Min(Items.Length - 1, selectedIndex + 1)); AcceptEvent(); }
            else if (k.Keycode is Key.Enter or Key.KpEnter && selectedIndex >= 0) { EmitSignal(SignalName.ItemClick, selectedIndex); AcceptEvent(); }
        }
    }
}
