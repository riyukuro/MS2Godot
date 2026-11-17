using Godot;
using Maple2.PacketLib.Tools;
using Ms2Godot.Look;
using Ms2.Net.Packets;
using Ms2Godot.Ui.Contracts;
using C = Ms2Godot.Ui.Contracts.UICharacterCreateDialogContract;

namespace Ms2Godot.Ui.Create;

/// <summary>
/// The customize page (uicharactercreatedialog), ported by hand from the movie's AS3 (CharacterCreateDialog,
/// CharacterCreatePage, CharacterCreateContent) and the C++ dialog (UICharacterCreateDialog):
/// gender radio (OnChangeGender), seven category buttons that open their content (OnChangeCategory, then the host's
/// buildItems / buildColors), item tiles (OnChangeItem), colour tiles (OnChangeColor), random (OnRandom), back to the
/// job pick (OnSceneLobbySelect) and create (OnCreate: the name prompt, max 12 characters, then the create request).
/// The request is built with Ms2.Net and handed to <see cref="Created"/>; nothing is sent.
/// Picking an editable item opens its option panel (<see cref="CustomOptionPanel"/>: size, angle, position presets);
/// the values go into the request. Not ported: the item guide movies (.usm) and the guide button.
/// </summary>
public sealed class CharacterCreateLogic
{
    const string Movie = C.Movie;
    const float ItemTile = 78, ColorW = 38, ColorH = 36, Spacing = 2;   // TileCacheList columnWidth / rowHeight / rendererSpacing
    const float CategoryStep = 44, MoverOffset = 340;                   // page buttons are 44 apart; charcreate_mc_mover moves 340

    static readonly string[] Contents = { "m_haircontent", "m_facecontent", "m_makeupcontent", "m_skincontent", "m_clothescontent", "m_pantscontent", "m_shoescontent" };

    readonly UiMovie movie;
    readonly MovieClipNode root, dialog, content, page;
    readonly CreateLook look;
    readonly List<(MovieClipNode Button, ButtonWidget Widget, MovieClipNode Content, Vector2 Home)> categories = new();
    readonly List<(Control Tile, int Id)> itemTiles = new();
    readonly List<(Control Tile, int Row)> colorTiles = new();
    readonly Dictionary<string, Texture2D> icons = new();
    ButtonWidget male, female;
    public ButtonWidget RandomButton, BackButton, CreateButton;
    int category = -1;
    readonly Random rng = new();
    double openTime;
    readonly CustomOptionPanel options;

    /// <summary>The look changed: rebuild the preview character.</summary>
    public event Action LookChanged;
    /// <summary>OnSceneLobbySelect: back to the job pick.</summary>
    public event Action Back;
    /// <summary>The create request (CharacterManagement, command 1), built and not sent.</summary>
    public event Action<CreateCharacter, byte[]> Created;

    public CreateLook Look => look;
    public int Category => category;

    public CharacterCreateLogic(UiMovie movie, MovieClipNode root, CreateLook look)
    {
        this.movie = movie; this.root = root; this.look = look;
        dialog = root.Children.FirstOrDefault(c => c.Node is MovieClipNode)?.Node as MovieClipNode ?? root;
        content = root.FindClip(C.Instances.M_content) ?? throw new InvalidOperationException("create: missing m_content");
        page = root.FindClip(C.Instances.M_content_m_page);
        male = content.FindClip("m_male")?.Widget as ButtonWidget;
        female = content.FindClip("m_female")?.Widget as ButtonWidget;
        RandomButton = content.FindClip("m_random")?.Widget as ButtonWidget;
        BackButton = content.FindClip("m_charlist")?.Widget as ButtonWidget;
        CreateButton = root.FindClip("m_create")?.Widget as ButtonWidget;
        IconButtons.Setup(movie, root.FindClip("m_guide"), "charcreate_icon_guide", "$s_charcreate_btn_guide");
        foreach (var (w, g) in new[] { (male, 0), (female, 1) })
            if (w != null) { w.Toggle = true; w.Click += _ => ChangeGender(g, true); }
        if (RandomButton != null) RandomButton.Click += _ => OnRandom();
        if (BackButton != null) BackButton.Click += _ => { UiBridge.Raise(Movie, C.ToHost.OnSceneLobbySelect); Back?.Invoke(); };
        if (CreateButton != null) CreateButton.Click += _ => OnCreate();
        options = new CustomOptionPanel(movie, IconAt);
        options.Changed += () => LookChanged?.Invoke();

        for (int i = 0; i < Contents.Length && page != null; i++)
        {
            var btn = page.FindClip($"__id{i + 1}_");
            var cont = page.FindClip(Contents[i]);
            if (btn == null) continue;
            var w = btn.Widget as ButtonWidget;
            if (w != null) { w.Toggle = true; int index = i; w.Click += _ => SelectCategory(index, true); }
            if (cont != null)
            {
                cont.Visible = false;
                // the TileCacheList placeholders draw nothing themselves; the tiles below stand in for their renderers
                foreach (var n in new[] { "m_editItemList", "m_colorList", "m_line", "m_movie" }) cont.SetNamedVisible(n, false);
            }
            categories.Add((btn, w, cont, btn.Position));
        }
    }

    /// <summary>initData(gender, serverInfo) and buildCurrentItems: the start of the page, then popup.</summary>
    public void InitData(int gender, int jobCode, string serverName)
    {
        openTime = Time.GetTicksMsec() / 1000.0;
        look.Reset(gender, jobCode);
        SetGenderButtons(gender);
        content.SetNamedText("m_serverName", serverName ?? "");
        content.SetNamedText("m_regionName", "");
        UiBridge.Raise(Movie, C.ToHost.OnChangeTab, 0);
        BuildCurrentItems();
        category = -1;
        SelectCategory(0, true);
        dialog.GotoLabel("popup", true);
        root.FindClip(C.Instances.M_title)?.GotoLabel("popup", true);
    }

    /// <summary>
    /// The page built again over a page already open (a window resize: the client keeps the dialog and aligns it again):
    /// the same look, category, option panel (<paramref name="optionsOpen"/>: it was showing) and name prompt, with no popup
    /// or open animation and no host events.
    /// </summary>
    public void Restore(CharacterCreateLogic old, string serverName, bool optionsOpen)
    {
        openTime = old.openTime;
        SetGenderButtons(look.Gender);
        content.SetNamedText("m_serverName", serverName ?? "");
        content.SetNamedText("m_regionName", "");
        BuildCurrentItems();
        category = -1;
        SelectCategory(Math.Max(old.category, 0), false, false);
        if (optionsOpen && CreateLook.CategorySlot[category] is var slot && look.Items.TryGetValue(slot, out int id) && look.Customize(id).Editable)
            options.Show(CurrentContent, look.ItemName(id), look.Customize(id), look.ValuesOf(id), false);
        dialog.GotoLabel("popup");
        root.FindClip(C.Instances.M_title)?.GotoLabel("popup");
        if (old.prompt != null)
        {
            string text = old.nameEdit?.Text ?? "", error = old.nameError?.Text ?? "";
            old.ClosePrompt();
            OpenPrompt();
            nameEdit.Text = text;
            nameEdit.CaretColumn = text.Length;
            nameError.Text = error;
        }
    }

    void SetGenderButtons(int gender)
    {
        male?.SetSelected(gender == 0, false);
        female?.SetSelected(gender == 1, false);
    }

    /// <summary>OnChangeGender: a fresh look for the gender (CSceneStateMulti::FUN_14174ec40), then onChangeGenderComplete.</summary>
    public void ChangeGender(int gender, bool raise)
    {
        SetGenderButtons(gender);
        if (gender == look.Gender) return;
        if (raise) UiBridge.Raise(Movie, C.ToHost.OnChangeGender, gender);
        look.Reset(gender, look.JobCode);
        options.Hide();
        LookChanged?.Invoke();
        BuildCurrentItems();
        // onChangeGenderComplete: initSelectedIndexPageAll + onChangeCategory
        int keep = Math.Max(category, 0);
        category = -1;
        SelectCategory(keep == 0 ? 0 : keep, true);
    }

    /// <summary>OnRandom(gender): a random item and colour in every category.</summary>
    public void OnRandom()
    {
        UiBridge.Raise(Movie, C.ToHost.OnRandom, look.Gender);
        look.Randomize(rng);
        options.Hide();
        LookChanged?.Invoke();
        BuildCurrentItems();
        if (category >= 0) { BuildItems(category); BuildColors(category); }
    }

    /// <summary>buildCurrentItems / updateCurrentItem: each category button shows the worn item's name and colour.</summary>
    void BuildCurrentItems()
    {
        for (int i = 0; i < categories.Count; i++)
        {
            var slot = CreateLook.CategorySlot[i];
            var btn = categories[i].Button;
            int id = look.Items.GetValueOrDefault(slot);
            string name = slot == EquipSlot.SK ? look.ItemName(CreateLook.SkinItem) : id == 0 ? "" : look.ItemName(id);
            btn.SetNamedText("m_item", name);
            SetColor(btn, SwatchOf(slot));
        }
    }

    /// <summary>
    /// CharacterCreateCategoryButton.setColor(argb, visible): m_color.m_colorBG gets the colour as its colorTransform and
    /// m_color shows only with a colour. Values a script sets stay over the button's state frames (Flash stops the timeline
    /// driving them), so they go on the placements, not the nodes the next GotoFrame resets.
    /// </summary>
    static void SetColor(MovieClipNode btn, Color? color)
    {
        foreach (var clip in btn.AllClips().ToList())
        {
            bool any = false;
            foreach (var swatch in clip.Children.Where(ch => ch.Name == "m_color"))
            {
                any = true;
                swatch.Hidden = color == null;
                if (color is not { } c || swatch.Node is not MovieClipNode sw) continue;
                foreach (var bg in sw.AllClips())
                {
                    foreach (var fill in bg.Children.Where(ch => ch.Name == "m_colorBG")) fill.BaseColor = c;
                    bg.GotoFrame(bg.Frame);
                }
            }
            if (any) clip.GotoFrame(clip.Frame);
        }
    }

    Color? SwatchOf(EquipSlot slot)
    {
        if (slot == EquipSlot.SK) return look.Skin is { } s ? ToColor(s.Primary) : null;
        if (!look.Items.TryGetValue(slot, out int id) || look.Palette(id).Palette == 0) return null;
        return ToColor(look.ColorOf(slot).Primary);
    }

    static Color ToColor(Rgba c) => new(c.R / 255f, c.G / 255f, c.B / 255f, 1f);

    /// <summary>CharacterCreatePage.onChangeCategory: the chosen content opens, the buttons below it move down.</summary>
    public void SelectCategory(int index, bool raise, bool animate = true)
    {
        if (index < 0 || index >= categories.Count) return;
        bool changed = index != category;
        category = index;
        for (int i = 0; i < categories.Count; i++)
        {
            var (btn, w, cont, home) = categories[i];
            w?.SetSelected(i == index, false);
            btn.Position = home + new Vector2(0, i > index ? MoverOffset : 0);
            if (cont != null)
            {
                cont.Visible = i == index;
                if (i == index && changed) cont.GotoLabel("_open", animate);
            }
        }
        if (raise) UiBridge.Raise(Movie, C.ToHost.OnChangeCategory, 0, index);
        if (changed) options.Hide();
        BuildItems(index);
        BuildColors(index);
    }

    MovieClipNode CurrentContent => category >= 0 && category < categories.Count ? categories[category].Content : null;

    /// <summary>buildItems(arr, select): the category's items as 78 x 78 tiles, five to a row.</summary>
    void BuildItems(int index)
    {
        foreach (var (t, _) in itemTiles) t.QueueFree();
        itemTiles.Clear();
        var cont = categories[index].Content;
        var list = cont?.FindClip("m_editItemList");
        if (cont == null || list == null) return;
        var slot = CreateLook.CategorySlot[index];
        var ids = look.SlotItems(slot);
        int columns = Math.Max(1, (int)(Math.Ceiling(100 * list.Scale.X) / ItemTile));
        int selected = look.Items.TryGetValue(slot, out int cur) ? ids.IndexOf(cur) : slot == EquipSlot.SK ? 0 : -1;
        for (int i = 0; i < ids.Count; i++)
        {
            var tile = ItemTileFor(ids[i], i == selected);
            tile.Position = list.Position + new Vector2(i % columns * (ItemTile + Spacing), i / columns * (ItemTile + Spacing));
            cont.AddChild(tile);
            itemTiles.Add((tile, ids[i]));
        }
        int rows = (ids.Count + columns - 1) / columns;
        itemRowsHeight = rows * (ItemTile + Spacing) - Spacing;
    }

    float itemRowsHeight;

    Control ItemTileFor(int id, bool selected)
    {
        var panel = new Panel { Size = new Vector2(ItemTile, ItemTile), MouseFilter = Control.MouseFilterEnum.Ignore };
        var box = new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.12f, 0.14f, 0.85f),
            BorderColor = selected ? new Color(1f, 0.82f, 0.2f) : new Color(0.35f, 0.35f, 0.38f),
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        };
        box.SetBorderWidthAll(selected ? 3 : 1);
        panel.AddThemeStyleboxOverride("panel", box);
        var tex = Icon(id);
        if (tex != null)
            panel.AddChild(new TextureRect
            {
                Texture = tex, Position = new Vector2(3, 3), Size = new Vector2(ItemTile - 6, ItemTile - 6),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        else
            panel.AddChild(new Label { Text = look.ItemName(id), Position = new Vector2(4, 4), Size = new Vector2(ItemTile - 8, ItemTile - 8), AutowrapMode = TextServer.AutowrapMode.WordSmart, ClipText = true });
        return panel;
    }

    /// <summary>The item's slotIcon from itemdata (Resource/Image).</summary>
    Texture2D Icon(int id) => IconAt(look.IconPath(id));

    /// <summary>A Resource/Image png by its data path (./Data/Resource/Image/...), cached.</summary>
    Texture2D IconAt(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (icons.TryGetValue(path, out var t)) return t;
        t = null;
        try
        {
            var packs = movie.Assets.Packs;
            string entry = path.Replace('\\', '/');
            int at = entry.IndexOf("Resource/Image/", StringComparison.OrdinalIgnoreCase);
            if (at >= 0) entry = entry[(at + "Resource/Image/".Length)..];
            string found = packs.Exists("Resource/Image", entry) ? entry
                : packs.Exists("Resource/Image", entry.ToLowerInvariant()) ? entry.ToLowerInvariant()
                : packs.FindByFileName("Resource/Image", System.IO.Path.GetFileName(entry));
            if (found != null)
            {
                var img = new Image();
                if (img.LoadPngFromBuffer(packs.Read("Resource/Image", found)) == Error.Ok) t = ImageTexture.CreateFromImage(img);
            }
        }
        catch (Exception ex) { GD.PushWarning($"icon {path}: {ex.Message}"); }
        icons[path] = t;
        return t;
    }

    /// <summary>buildColors(arr{realColor, visibleColor}, select): the palette rows, ten to a row, under the item tiles.</summary>
    void BuildColors(int index)
    {
        foreach (var (t, _) in colorTiles) t.QueueFree();
        colorTiles.Clear();
        var cont = categories[index].Content;
        var list = cont?.FindClip("m_editItemList");
        var colorList = cont?.FindClip("m_colorList");
        if (cont == null || list == null) return;
        var slot = CreateLook.CategorySlot[index];
        int palette = slot == EquipSlot.SK ? ColorPalettes.SkinPalette
            : look.Items.TryGetValue(slot, out int id) ? look.Palette(id).Palette : 0;
        var rows = palette == 0 ? Array.Empty<(Rgba, Rgba, Rgba)>() : look.PaletteRows(palette);
        if (rows.Count == 0) return;
        float width = 100 * (colorList?.Scale.X ?? list.Scale.X);
        int columns = Math.Max(1, (int)(Math.Ceiling(width + Spacing) / (ColorW + Spacing)));
        // m_line sits 4 under the item rows, the colours 3 under the line
        float top = list.Position.Y + (itemTiles.Count > 0 ? itemRowsHeight + 4 + 2 + 3 : 0);
        int selected = look.RowOf(slot);
        for (int i = 0; i < rows.Count; i++)
        {
            var (c0, _, _) = rows[i];
            var tile = new Panel { Size = new Vector2(ColorW, ColorH), MouseFilter = Control.MouseFilterEnum.Ignore };
            var box = new StyleBoxFlat
            {
                BgColor = ToColor(c0),
                BorderColor = i == selected ? new Color(1f, 0.82f, 0.2f) : new Color(0.1f, 0.1f, 0.1f, 0.8f),
                CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
            };
            box.SetBorderWidthAll(i == selected ? 3 : 1);
            tile.AddThemeStyleboxOverride("panel", box);
            tile.Position = new Vector2(list.Position.X + i % columns * (ColorW + Spacing), top + i / columns * (ColorH + Spacing));
            cont.AddChild(tile);
            colorTiles.Add((tile, i));
        }
    }

    /// <summary>Slider drags in the option panel, then clicks on the item and colour tiles (TileCacheList INDEX_CHANGE).</summary>
    public bool OnMouse(InputEvent e)
    {
        if (options.OnMouse(e)) return true;
        if (e is not InputEventMouseButton mb || !mb.Pressed || mb.ButtonIndex != MouseButton.Left || category < 0) return false;
        foreach (var (tile, id) in itemTiles)
            if (tile.IsVisibleInTree() && tile.GetGlobalRect().HasPoint(mb.Position)) { ChangeItem(id); return true; }
        foreach (var (tile, row) in colorTiles)
            if (tile.IsVisibleInTree() && tile.GetGlobalRect().HasPoint(mb.Position)) { ChangeColor(row); return true; }
        return false;
    }

    /// <summary>OnChangeItem(itemId): wear the item with its default colour row.</summary>
    public void ChangeItem(int id)
    {
        var slot = CreateLook.CategorySlot[category];
        UiBridge.Raise(Movie, C.ToHost.OnChangeItem, id.ToString());
        if (slot == EquipSlot.SK) { options.Hide(); return; }
        look.Items[slot] = id;
        look.Rows[slot] = look.Palette(id).DefaultRow;
        LookChanged?.Invoke();
        BuildCurrentItems();
        BuildItems(category);
        BuildColors(category);
        // onChangeItem: an editable item plays its movie (the panel pops up), then the host's buildCustom
        var custom = look.Customize(id);
        if (custom.Editable) options.Show(CurrentContent, look.ItemName(id), custom, look.ValuesOf(id));
        else options.Hide();
    }

    /// <summary>OnChangeColor(index): the palette row of the open category.</summary>
    public void ChangeColor(int row)
    {
        var slot = CreateLook.CategorySlot[category];
        UiBridge.Raise(Movie, C.ToHost.OnChangeColor, row);
        if (slot == EquipSlot.SK) look.SkinRow = row; else look.Rows[slot] = row;
        LookChanged?.Invoke();
        BuildCurrentItems();
        BuildColors(category);
    }

    // ---- create: OnCreate opens the name prompt (CMsgBoxData input, max 12 characters) ----

    Control prompt;
    LineEdit nameEdit;
    Label nameError;

    public bool PromptOpen => prompt != null;
    /// <summary>The name typed in the open prompt (scripted runs).</summary>
    public string PromptText { get => nameEdit?.Text; set { if (nameEdit != null) { nameEdit.Text = value; nameEdit.CaretColumn = value.Length; } } }
    public CustomOptionPanel Options => options;

    /// <summary>OnCreate: the name prompt over the page.</summary>
    public void OnCreate()
    {
        UiBridge.Raise(Movie, C.ToHost.OnCreate);
        OpenPrompt();
    }

    void OpenPrompt()
    {
        if (prompt != null) return;
        var layer = root.GetParent();
        var size = root.GetViewportRect().Size;
        prompt = new Panel { Size = new Vector2(420, 170), Position = size / 2 - new Vector2(210, 85) };
        var box = new StyleBoxFlat { BgColor = new Color(0.1f, 0.1f, 0.12f, 0.95f), BorderColor = new Color(0.55f, 0.5f, 0.35f) };
        box.SetBorderWidthAll(2); box.SetCornerRadiusAll(6);
        prompt.AddThemeStyleboxOverride("panel", box);
        prompt.AddChild(new Label { Text = movie.Assets.Str("$s_charcreate_input_name") is var s && !s.StartsWith('$') ? s : "Character name", Position = new Vector2(20, 14), Size = new Vector2(380, 24) });
        nameEdit = new LineEdit { MaxLength = 12, Position = new Vector2(20, 46), Size = new Vector2(380, 34) };
        nameEdit.TextSubmitted += t => Confirm(t);
        prompt.AddChild(nameEdit);
        nameError = new Label { Position = new Vector2(20, 84), Size = new Vector2(380, 22), Modulate = new Color(1f, 0.45f, 0.4f) };
        prompt.AddChild(nameError);
        var ok = new Button { Text = "OK", Position = new Vector2(110, 118), Size = new Vector2(90, 34) };
        ok.Pressed += () => Confirm(nameEdit.Text);
        var cancel = new Button { Text = "Cancel", Position = new Vector2(220, 118), Size = new Vector2(90, 34) };
        cancel.Pressed += ClosePrompt;
        prompt.AddChild(ok); prompt.AddChild(cancel);
        layer.AddChild(prompt);
        nameEdit.GrabFocus();
    }

    public void ClosePrompt() { prompt?.QueueFree(); prompt = null; nameEdit = null; nameError = null; }

    /// <summary>The server's refusal of a create request: the name prompt opens again with the message under the input.</summary>
    public void ShowCreateError(string message)
    {
        if (prompt == null) OnCreate();
        if (nameError != null) nameError.Text = message ?? "";
    }

    /// <summary>Types a name into the open prompt and confirms it (scripted runs).</summary>
    public CreateCharacter Confirm(string name)
    {
        var err = CheckName(name);
        if (err != null) { if (nameError != null) nameError.Text = err; return null; }
        // UICharacterCreateDialog::FUN_1418e3f50: the request, then requestCreate
        int elapsed = (int)(Time.GetTicksMsec() / 1000.0 - openTime);
        var packet = look.Packet(name, elapsed);
        var w = new ByteWriter();
        w.WriteShort((short)packet.Op);
        packet.Write(w);
        var bytes = w.ToArray();
        GD.Print($"create request (not sent): {packet.Op} {bytes.Length} bytes, gender {packet.Gender}, job {packet.JobGroup}, name \"{name}\", " +
                 $"items {string.Join(" ", CreateCharacter.DecodeItems(packet.Items).Select(i => $"{i.Slot}:{i.Id}/{i.Color.Index}" +
                 (i.Appearance.Length > 0 ? "[" + string.Join(",", i.Appearance.Select(f => f.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))) + "]" : "")))}");
        GD.Print("  " + Convert.ToHexString(bytes));
        ClosePrompt();
        Created?.Invoke(packet, bytes);
        return packet;
    }

    Ms2.Formats.Text.BanTextChecker nameChecker;

    /// <summary>
    /// The create dialog's name check (UICharacterCreateDialog::FUN_1418e6430): an empty name gives common string 0xd,
    /// otherwise FUN_141276a30 runs the client's text checker (BanTextChecker.CheckCharacterName) over the locale's
    /// word tables; its message is shown under the input. Returns null when the name passes.
    /// </summary>
    public string CheckName(string name)
    {
        name ??= "";
        nameChecker ??= Ms2.Formats.Text.BanTextChecker.FromPacks(movie.Assets.Packs, movie.Assets.Language);
        if (name.Length == 0) return nameChecker.CommonString("s_char_err_input") ?? "Enter a name.";
        return nameChecker.CheckCharacterName(name, out var message) == 0 ? null : message;
    }
}
