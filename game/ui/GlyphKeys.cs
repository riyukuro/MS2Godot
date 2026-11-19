using Godot;

namespace Ms2Godot.Ui;

/// <summary>
/// The keys of the glyph dictionaries TextServer.ShapedTextGetGlyphs returns, made once. An indexer given a string converts
/// it to a new Variant (with a finalizer-owned disposer) on every read; a static Variant does not.
/// </summary>
static class GlyphKeys
{
    public static readonly Variant Start = "start";
    public static readonly Variant End = "end";
    public static readonly Variant Repeat = "repeat";
    public static readonly Variant Count = "count";
    public static readonly Variant Advance = "advance";
    public static readonly Variant Index = "index";
    public static readonly Variant Flags = "flags";
    public static readonly Variant FontRid = "font_rid";
    public static readonly Variant Offset = "offset";
}
