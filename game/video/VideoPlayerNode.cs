using Godot;
using Ms2.Formats.Pack;
using Ms2.Video;

namespace Ms2Godot;

/// <summary>
/// Shows a .usm movie (the client's scaleform VideoPlayer): decoded on a worker thread (Ms2.Video.VideoPlayback), the
/// Y, Cb and Cr planes uploaded as three L8 textures each new frame and converted to RGB in yuv_video.gdshader. The
/// picture is stretched to <see cref="Size"/> (local units, like a Flash Video's width and height); the clock is the
/// movie's frame rate from its USM header. No sound (the job previews have none; see <see cref="UsmVideo.HasAudio"/>).
/// </summary>
public partial class VideoPlayerNode : Node2D
{
    /// <summary>The client's movie archives (CommonPath.movie "..\Movie\" plus the file name).</summary>
    static readonly string[] Archives = { "Resource/Movie/Common", "Resource/Movie/Item", "Resource/Movie/Emotion" };
    static Shader shader;

    VideoPlayback playback;
    Image yImg, cbImg, crImg;
    ImageTexture yTex, cbTex, crTex;
    ShaderMaterial material;
    double clock;
    bool finishedRaised;
    Vector2 size = new(320, 240);
    int picW, picH;

    /// <summary>The movie ended (not raised while looping).</summary>
    public event Action Finished;

    /// <summary>Drawn size in local units.</summary>
    public Vector2 Size { get => size; set { size = value; QueueRedraw(); } }
    public UsmVideo Video => playback?.Video;
    public bool Playing => playback != null && !playback.Finished;
    /// <summary>Seconds since the movie started (keeps counting across loops).</summary>
    public double Time => clock;
    /// <summary>The movie name last passed to <see cref="Play(PackSet, string, bool)"/>.</summary>
    public string MovieName { get; private set; }

    public VideoPlayerNode() { Name = "Video"; TextureFilter = TextureFilterEnum.Linear; }

    /// <summary>Reads and demuxes a movie by file name (e.g. "jobPreview_80.usm") from the movie archives; null if absent.</summary>
    public static UsmVideo Load(PackSet packs, string movie)
    {
        if (packs == null || string.IsNullOrEmpty(movie)) return null;
        foreach (var archive in Archives)
        {
            string entry;
            try { entry = packs.FindByFileName(archive, movie); }
            catch (FileNotFoundException) { continue; }
            if (entry != null) return UsmVideo.Open(packs.Read(archive, entry));
        }
        return null;
    }

    /// <summary>VideoPlayer.play(url): loads the movie and starts it from the beginning. False if it cannot be read.</summary>
    public bool Play(PackSet packs, string movie, bool loop)
    {
        if (playback != null && MovieName == movie && Playing) return true;
        UsmVideo v;
        try { v = Load(packs, movie); }
        catch (Exception e) when (e is IOException or InvalidDataException or NotSupportedException)
        {
            GD.PushWarning($"movie {movie}: {e.Message}");
            v = null;
        }
        MovieName = movie;
        if (v == null) { Stop(); GD.PushWarning($"movie {movie}: not found or not playable"); return false; }
        Play(v, loop);
        return true;
    }

    /// <summary>Starts an opened movie.</summary>
    public void Play(UsmVideo video, bool loop)
    {
        Stop();
        playback = new VideoPlayback(video, loop);
        clock = 0;
        finishedRaised = false;
        Visible = true;
    }

    /// <summary>VideoPlayer.stop: ends playback and clears the picture.</summary>
    public void Stop()
    {
        playback?.Dispose();
        playback = null;
        picW = picH = 0;
        QueueRedraw();
    }

    /// <summary>Scripted runs: wait for the first frame and show it now.</summary>
    public bool ShowFirstFrame(int timeoutMs = 2000)
    {
        if (playback == null || !playback.WaitFirstFrame(timeoutMs)) return false;
        if (playback.Update(clock) is { } f) Upload(f);
        return picW > 0;
    }

    public override void _Process(double delta)
    {
        if (playback == null) return;
        clock += delta;
        var f = playback.Update(clock);
        if (f != null) Upload(f);
        else if (playback.Finished && !finishedRaised)
        {
            finishedRaised = true;
            Finished?.Invoke();
        }
    }

    void Upload(YuvFrame f)
    {
        Plane(ref yImg, ref yTex, f.Y, f.LumaStride, f.LumaHeight);
        Plane(ref cbImg, ref cbTex, f.Cb, f.ChromaStride, f.ChromaHeight);
        Plane(ref crImg, ref crTex, f.Cr, f.ChromaStride, f.ChromaHeight);
        if (material == null)
        {
            shader ??= GD.Load<Shader>("res://shaders/yuv_video.gdshader");
            material = new ShaderMaterial { Shader = shader };
            Material = material;
        }
        material.SetShaderParameter("tex_cb", cbTex);
        material.SetShaderParameter("tex_cr", crTex);
        picW = f.Width; picH = f.Height;
        QueueRedraw();
    }

    static void Plane(ref Image img, ref ImageTexture tex, byte[] data, int w, int h)
    {
        if (img == null || img.GetWidth() != w || img.GetHeight() != h)
        {
            img = Image.CreateFromData(w, h, false, Image.Format.L8, data);
            tex = ImageTexture.CreateFromImage(img);
            return;
        }
        img.SetData(w, h, false, Image.Format.L8, data);
        tex.Update(img);
    }

    public override void _Draw()
    {
        if (picW <= 0 || yTex == null) return;
        DrawTextureRectRegion(yTex, new Rect2(Vector2.Zero, size), new Rect2(0, 0, picW, picH));
    }

    public override void _ExitTree()
    {
        Stop();
        base._ExitTree();
    }
}
