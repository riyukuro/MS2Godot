using Ms2.Avm2;

namespace Ms2.Ui;

/// <summary>
/// GFxMovieMouseCursor (gfxui/gfxmoviemousecursor.cpp; movie id 6, ./Data/Resource/gfx/CommonMouseCursor.gfx,
/// gfxmanager.cpp:91, 521, loaded by LoadMouseCursorMovie :1151). This is the C++ side of the software cursor movie the
/// client loads once for the run. The movie's root sends "initializeClass"(root, cursor) (vfunc_4 :62-75), and the cursor
/// clip (scaleform.ms2.entity.MouseCursor) becomes the ASMouseCursor (gfxui/asmousecursor.cpp). That clip follows the
/// stage's mouse and shows one labelled frame of its m_enums: hand, disable, move, text, rotate, drag, resizeLT, resizeRT,
/// sell, talk, gamepad, interact. The other movies' "MouseCursor" calls come here (GFxMovieMain::vfunc_4,
/// gfxmoviemain.cpp:139-146, <see cref="UiHost.MouseCursor"/>).
/// </summary>
public sealed class MouseCursorController : UiController, ITooltipMovie
{
    public override string Movie => "CommonMouseCursor";
    protected override string EntityMethod => "changeType";
    protected override bool IsDialog => false;
    /// <summary>A GFxMovie, no UISwfEntity: no load visibility step.</summary>
    public override bool LoadSetsVisibility => false;

    /// <summary>The cursor types (Unk_MouseCursorType): the hand, and interact, the one the mouse picks' hover sets
    /// (CMouseController::UpdateHover, unit_140ad1860_mousecontroller.cpp:690-698).</summary>
    public const int Hand = 0, Interact = 0xb;

    AsObject root;                  // m_90
    AsObject cursor;                // m_30, the ASMouseCursor

    /// <summary>The cursor clip is known (initializeClass came).</summary>
    public bool Ready => cursor != null;

    /// <summary>Raised once the cursor clip is known.</summary>
    public event System.Action CursorReady;

    static bool Is(string a, string b) => string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>vfunc_4 @0x1422f2a50: "initializeClass"(root, cursor) keeps both; the cursor becomes the movie's mouse cursor
    /// (SetMouseCursor).</summary>
    public override bool OnMovieEvent(string ev, object[] args)
    {
        if (!Is(ev, "initializeClass")) return false;
        if (args.Length > 1)
        {
            root = args[0] as AsObject;
            cursor = args[1] as AsObject;
            if (cursor != null) CursorReady?.Invoke();
        }
        return true;
    }

    /// <summary>vfunc_3 @0x1422f2730 (nothing before the cursor is known): "fireCursorEvent"(type), "setText"(text) and
    /// "fireCursorMouseEvent"(label) go to the cursor clip.</summary>
    public void Call(string method, object[] args)
    {
        if (cursor == null || args.Length < 1) return;
        if (Is(method, "fireCursorEvent")) Host.Invoke(cursor, "fireCursorEvent", args[0]);
        else if (Is(method, "setText")) Host.Invoke(cursor, "setText", args[0]);
        else if (Is(method, "fireCursorMouseEvent") && args[0] is string label) Host.Invoke(cursor, "fireCursorMouseEvent", label);
    }

    /// <summary>ASMouseCursor::ChangeType @0x1422cee50 (asmousecursor.cpp:100-106): the clip's changeType(type).</summary>
    public void ChangeType(int type)
    {
        if (cursor != null) Host.Invoke(cursor, "changeType", (uint)type);
    }

    /// <summary>ASMouseCursor::SetText @0x1422ceea0 (:109-115): the clip's setText, the hint beside the cursor.</summary>
    public void SetText(string text)
    {
        if (cursor != null) Host.Invoke(cursor, "setText", text ?? "");
    }

    /// <summary>ASMouseCursor::GetCursorType @0x1422cef10 (:118-124): the index of the clip's current label in its m_enums; -1
    /// before the cursor is known.</summary>
    public int GetCursorType() => cursor == null ? -1 : (int)Rt.ToNumber(Host.Invoke(cursor, "getCursorType"));

    /// <summary>
    /// CMouseController::UpdateHover's cursor part (unit_140ad1860_mousecontroller.cpp:684-699): only while the cursor shows the
    /// hand or interact, it becomes interact when a pick is under the mouse (<paramref name="kind"/> not 0) and the hand
    /// otherwise, and a hint that is not empty is set. Returns the type set, or null when the cursor was left alone.
    /// </summary>
    public static int? HoverType(int current, int kind) =>
        current is Hand or Interact ? (kind != 0 ? Interact : Hand) : null;
}
