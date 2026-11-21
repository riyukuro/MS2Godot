using Godot;
using Ms2.Avm2.Flash;

namespace Ms2Godot.Ui;

/// <summary>
/// The system clipboard behind the movies' text fields. The client's GFxClipboard writes each copied or cut text to the
/// Windows clipboard as CF_UNICODETEXT (OnTextStore @0x1422ecd70, gfxvideo.cpp:106) and its clipboard viewer reads the
/// Windows clipboard back on every change (WM_DRAWCLIPBOARD, GFxMovieEventHandler vfunc_24 @0x1422da0a0,
/// gfxmovieeventhandler.cpp:378), so a paste takes what any program copied last. Here both go through DisplayServer.
/// </summary>
public sealed class SystemClipboard : ITextClipboard
{
    public static readonly SystemClipboard Instance = new();

    public string GetText() => DisplayServer.ClipboardGet();

    public void SetText(string text) => DisplayServer.ClipboardSet(text ?? "");
}
