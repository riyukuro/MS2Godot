using System.Text.Json.Nodes;
using Godot;
using Ms2.Formats.Pack;

namespace Ms2Godot.Ui.Native;

/// <summary>
/// Shared state of the native UI: the player's assets and the art baker. A native scene's root calls <see cref="Init"/>
/// in _EnterTree (before its widgets' _Ready). Native scenes lay out in stage units and let Godot scale the canvas
/// (<see cref="UseStageScale"/>), so text is rasterised at screen size.
/// </summary>
public static class NativeUi
{
    public static UiAssets Assets { get; private set; }
    public static NativeArt Art { get; private set; }

    /// <summary>Opens the player's data (MS2_DATA). False when it is not set.</summary>
    public static bool Init()
    {
        if (Art != null) return true;
        var data = Settings.DataDir;
        if (string.IsNullOrEmpty(data)) { GD.PushError("Set MS2_DATA (the client's Data folder), or fill in local.cfg."); return false; }
        Assets = new UiAssets(PackSet.Shared(data));
        Art = new NativeArt(Assets);
        return true;
    }

    /// <summary>
    /// Canvas scaling as the client scales its movies: the stage is the window divided by the UI scale
    /// (<see cref="UiScale"/>), or a fixed stage height for the lobby dialogs (Common.getLobbyRatioY uses 1024).
    /// </summary>
    public static void UseStageScale(Window w, float stageHeight = 0)
    {
        var size = (Vector2)w.Size;
        var stage = stageHeight > 0 ? new Vector2(size.X * stageHeight / size.Y, stageHeight) : UiScale.Stage(size);
        w.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
        w.ContentScaleAspect = Window.ContentScaleAspectEnum.Expand;
        w.ContentScaleSize = new Vector2I((int)Math.Round(stage.X), (int)Math.Round(stage.Y));
    }

    /// <summary>A flash string key ("$s_...") through the string table, other text as is.</summary>
    public static string Str(string s) => Assets == null || string.IsNullOrEmpty(s) ? s ?? "" : Assets.Str(s);

    /// <summary>Regular.easeOut of fl.transitions (quadratic).</summary>
    public static Tween EaseOut(Node owner) => owner.CreateTween().SetIgnoreTimeScale().SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);

    /// <summary>A Label formatted as a movie text field (font class, size, colour, alignment, wrap, glow/shadow filters).</summary>
    public static Label MakeLabel(JsonNode def, JsonNode filters = null)
    {
        var font = Assets?.FontFor((string)def?["fontClass"]);
        float size = UiBuilder.F(def?["size"], 12);
        var ls = new LabelSettings { Font = font, FontSize = Math.Max(1, (int)Math.Round(size)), FontColor = UiBuilder.ParseColor((string)def?["color"], Colors.Black) };
        foreach (var f in filters as JsonArray ?? new JsonArray())
        {
            var type = (string)f["type"];
            var col = UiBuilder.ParseColor((string)f["color"], Colors.Black);
            float blur = Math.Max(UiBuilder.F(f["blurX"], 2), UiBuilder.F(f["blurY"], 2));
            if (type == "glow") { ls.OutlineSize = Math.Max(1, (int)Math.Round(Math.Min(blur, 4))); ls.OutlineColor = col; }
            else if (type == "dropShadow") { ls.ShadowColor = col; ls.ShadowOffset = new Vector2(1, 1); ls.ShadowSize = Math.Max(1, (int)Math.Round(Math.Min(blur, 3))); }
        }
        return new Label
        {
            LabelSettings = ls, MouseFilter = Control.MouseFilterEnum.Ignore, ClipText = false,
            HorizontalAlignment = (string)def?["align"] switch { "center" => HorizontalAlignment.Center, "right" => HorizontalAlignment.Right, _ => HorizontalAlignment.Left },
            VerticalAlignment = VerticalAlignment.Top,
            AutowrapMode = (bool)(def?["wordWrap"] ?? false) ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
        };
    }

    /// <summary>Places a label at a text field's rect (symbol units). The game font's line box sits like the system font's (see TextNode).</summary>
    public static Label PlaceLabel(Control parent, NativeTextDef t, Vector2 offset, string text = null)
    {
        var l = MakeLabel(t.Def, t.Filters);
        float size = UiBuilder.F(t.Def?["size"], 12);
        l.Position = t.Rect.Position + offset + new Vector2(0, -0.12f * size);
        l.Size = t.Rect.Size;
        l.Text = text ?? Str((string)t.Def?["initial"] ?? "");
        parent.AddChild(l);
        return l;
    }
}
