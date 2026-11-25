namespace Ms2.Ui;

/// <summary>
/// ASTooltip (client/ms2/astooltip.cpp): the tooltip data object a dialog gives the movie's TooltipManager, which passes it
/// to the tooltip movie (CommonTooltip.createTooltip: "tooltip" names the class to make, the rest is its setData).
/// </summary>
public sealed class AsTooltip : Dictionary<string, object>
{
    /// <summary>ApplyTextTooltip @0x1409e26b0: nothing for an empty text; else the text tooltip class
    /// (SetTooltipClass), the text (SetText), SetOnDelay(300), SetOffDelay(60000).</summary>
    public bool ApplyTextTooltip(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        this["tooltip"] = "export_common_tooltip_tooltipText";
        this["text"] = text;
        this["onDelay"] = 300;
        this["offDelay"] = 60000;
        return true;
    }

    /// <summary>SetTooltipAlign @0x1409c6dc0: the int member "tooltipAlign".</summary>
    public void SetTooltipAlign(int align) => this["tooltipAlign"] = align;

    /// <summary>ApplyTextTooltip into a new object: null when the text is empty (the caller's return value stays unset).</summary>
    public static AsTooltip Text(string text)
    {
        var t = new AsTooltip();
        return t.ApplyTextTooltip(text) ? t : null;
    }
}

/// <summary>GFxMovieTooltip::vfunc_3: a "Tooltip" call from a movie (createTooltip / destroyTooltip), its objects already
/// copied out of the calling movie (<see cref="UiHost.FromAs"/>). GFxMovieMouseCursor::vfunc_3 takes the "MouseCursor" calls
/// the same way.</summary>
public interface ITooltipMovie
{
    void Call(string method, object[] args);
}
