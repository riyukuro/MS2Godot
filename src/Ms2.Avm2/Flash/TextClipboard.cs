namespace Ms2.Avm2.Flash;

/// <summary>
/// The clipboard the text fields copy to and paste from (GFx::TextClipboard, the state the loader installs and every text
/// field's EditorKit shares, sfr_gfx_03e.cpp:573). The client's subclass GFxClipboard writes each stored text to the
/// Windows clipboard (OnTextStore @0x1422ecd70, gfxvideo.cpp:106), and the window's clipboard viewer copies the Windows
/// clipboard back into it whenever that changes (WM_DRAWCLIPBOARD, GFxMovieEventHandler vfunc_24 @0x1422da0a0), so a
/// paste reads what the system clipboard holds. The game gives the system clipboard; tests give their own.
/// </summary>
public interface ITextClipboard
{
    /// <summary>The plain text a paste inserts (TextClipboard::GetText).</summary>
    string GetText();

    /// <summary>Stores a copied or cut text (TextClipboard::SetText).</summary>
    void SetText(string text);
}

/// <summary>GFx::TextClipboard's own plain-text store (@0x14258d510), with no system clipboard behind it.</summary>
public sealed class TextClipboard : ITextClipboard
{
    string text = "";

    public string GetText() => text;

    public void SetText(string text) => this.text = text ?? "";
}
