using Godot;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;

namespace Ms2Godot.Ui;

/// <summary>
/// The picture of a movie's scaleform.ms2.video.VideoPlayer (a flash.media.Video) on a <see cref="MovieDialog"/>. The AVM2
/// player's Video and NetStream keep only the AS side working, so the host plays the stream: the VideoPlayer's m_url
/// (set by play(url), the movie archive's file) while m_stop is false, looped when m_loop is set, drawn over the dialog
/// at the Video's place on the stage (its global matrix over its 0,0 - width,height box) and at its concatenated alpha.
/// The Video is drawn on top of the dialog, not in its display list order.
/// </summary>
public partial class MovieVideo : Node2D
{
    readonly MovieDialog dialog;
    readonly Func<object> find;
    readonly PackSet packs;
    readonly VideoPlayerNode player = new();
    string url;

    /// <summary>The player drawing the picture.</summary>
    public VideoPlayerNode Player => player;

    /// <param name="find">The VideoPlayer object, or null while there is none.</param>
    public MovieVideo(MovieDialog dialog, PackSet packs, Func<object> find)
    {
        this.dialog = dialog; this.packs = packs; this.find = find;
        Name = "MovieVideo";
        AddChild(player);
    }

    /// <summary>The VideoPlayer's own field (declared in its package: scaleform.ms2.video).</summary>
    object Field(object v, string name)
    {
        object r = null;
        var n = new Name(name, new[] { Ns.Public, Ns.Protected, Ns.Get(NsKind.Internal, "scaleform.ms2.video") });
        dialog.Host.RT.Guard("get " + name, () => r = dialog.Host.RT.GetProperty(v, n));
        return r;
    }

    public override void _Process(double delta) => Sync();

    /// <summary>Follows the VideoPlayer now (play, stop, place, alpha).</summary>
    public void Sync()
    {
        var v = find() as AsVideo;
        bool playing = v != null && v.OnStage && !Rt.ToBoolean(Field(v, "m_stop"));
        string next = playing ? Rt.ToString(Field(v, "m_url")) : null;
        if (next != url)
        {
            url = next;
            if (url == null) player.Stop();
            else player.Play(packs, url, Rt.ToBoolean(Field(v, "m_loop")));
        }
        if (!playing || !v.EffectivelyVisible) { Visible = false; return; }
        double alpha = 1;
        for (AsDisplayObject o = v; o != null; o = o.Parent) alpha *= o.Color.Am;
        var m = v.ToGlobalMatrix();
        var b = v.LocalBounds();
        float s = UiScale.For(GetViewport().GetVisibleRect().Size);
        // stage to window: GFxMovie's placement as MovieDialog does it (the stage origin at MovieOrigin, at the UI scale)
        Transform = new Transform2D(new Vector2((float)m.A, (float)m.B) * s, new Vector2((float)m.C, (float)m.D) * s,
            UiScale.MovieOrigin + new Vector2((float)m.Tx, (float)m.Ty) * s);
        player.Position = new Vector2((float)b.X, (float)b.Y);
        player.Size = new Vector2((float)b.W, (float)b.H);
        Modulate = new Color(1, 1, 1, (float)Math.Clamp(alpha, 0, 1));
        Visible = true;
    }
}
