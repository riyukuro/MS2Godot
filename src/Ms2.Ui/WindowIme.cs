using Ms2.Avm2.Flash;

namespace Ms2.Ui;

/// <summary>
/// The window's IME, switched with the movies' focus as IMEManager::HandleFocus does (@0x142543f90,
/// sfr_as3_20.cpp:395: EnableIME when the new focus is a text field). Godot's window IME is also where its X11 display
/// server composes dead keys (a circumflex or an accent key, then a space or a vowel): while it is off the dead key
/// comes as a key without a character and the composed one never comes. In the client Windows composes them for every
/// focused field (WM_DEADCHAR, then the composed WM_CHAR into the Scaleform field), with the IME on or off, so here the
/// window's IME is on while any input field has the focus, a password field too.
/// On X11 the engine's IME takes the keys through a child window that holds the input focus; the engine gives the focus
/// back to the main window on a mouse press and once shortly after it first maps that child, so the window is asked
/// again after a switch on and after each mouse press (asking while it holds the focus changes nothing).
/// STILL-OPEN: there the client's EnableIME(false) also keeps a Hangul IME out of a password or IME-disabled field;
/// here an IME can compose into one. The composition string is not drawn in the field (the client's candidate list
/// and inline composition), only its committed characters arrive.
/// </summary>
public sealed class WindowIme
{
    /// <summary>Frames after a switch on until the window is asked once more (the engine's focus move comes after
    /// the first map, about 30 ms on).</summary>
    public const int ConfirmFrames = 15;

    readonly Action<bool> set;
    bool on;                    // what the window was last asked
    int confirm;                // frames until it is asked again, 0: not

    /// <param name="set">Switches the window's IME (DisplayServer.WindowSetImeActive).</param>
    public WindowIme(Action<bool> set) => this.set = set;

    /// <summary>Whether a movie's focus takes typed text: an input field still on its stage.</summary>
    public static bool TakesText(object focus) => focus is AsTextField { type: "input" } tf && tf.OnStage;

    /// <summary>
    /// Once a frame: switches the window's IME when <paramref name="want"/> changed. It is not switched off while
    /// <paramref name="otherOwnsIt"/> (an engine text control has the focus and keeps its own IME on; asked only then).
    /// While on, it is asked again <see cref="ConfirmFrames"/> frames after the switch and on the frame a mouse button
    /// went down (<paramref name="mousePressed"/>).
    /// </summary>
    public void Sync(bool want, Func<bool> otherOwnsIt, bool mousePressed = false)
    {
        if (want != on)
        {
            on = want;
            confirm = want ? ConfirmFrames : 0;
            if (want || !otherOwnsIt()) set(want);
            return;
        }
        if (!on) return;
        if (mousePressed) confirm = 1;
        if (confirm > 0 && --confirm == 0) set(true);
    }

    /// <summary>
    /// The window gained or lost the focus: a lost focus turns the window's IME off, and one asked for while the
    /// window had no focus was not taken, so the next <see cref="Sync"/> asks again.
    /// </summary>
    public void WindowFocusChanged() => on = false;
}
