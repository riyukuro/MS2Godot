using Godot;
using Ms2.Formats.Pack;
using Ms2.Video;

namespace Ms2Godot;

/// <summary>
/// Shows a .usm movie (the client's scaleform VideoPlayer): decoded on a worker thread (Ms2.Video.VideoPlayback), the
/// Y, Cb and Cr planes uploaded as three L8 textures each new frame and converted to RGB in yuv_video.gdshader. The
/// picture is stretched to <see cref="Size"/> (local units, like a Flash Video's width and height); the clock is the
/// movie's frame rate from its USM header.
/// Sound: the usm's @SFA stream is a whole ADX file (<see cref="Ms2.Audio.AdxStream"/>). The client sets the player's volume
/// before it plays (ASVideoPlayer::SetVolume @0x1422e9490, asvideoplayer.cpp:64; UICutSceneMovie::PlayUsm @0x14190ffc0,
/// uicutscenemovie.cpp:392-404) and the CRI library streams the audio; here <see cref="SoundVolume"/> is that volume (0 by default:
/// the hud videos and job previews that set none stay silent). The stream is decoded as it plays, into an AudioStreamGenerator
/// that is topped up a tenth of a second at a time, so the 32 MB of PCM of a 190 s movie are never in memory
/// (resident: the usm file's own ADX bytes, which the demuxed movie holds anyway, and the generator's one second); the player
/// starts with the picture and loops with it.
/// STILL-OPEN: the sound is not on <see cref="SoundBuses.Movie"/> (its gain is the volume alone); the movie's second audio
/// track (VideoPlayer::SubAudio); pause and seek (the sound does not follow them).
/// </summary>
public partial class VideoPlayerNode : Node2D
{
    static readonly StringName TexCb = "tex_cb", TexCr = "tex_cr";
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
    AudioStreamPlayer soundPlayer;
    AudioStreamGeneratorPlayback soundOut;
    Ms2.Audio.AdxStream soundStream;
    short[] soundPcm;
    Vector2[] soundFrames;
    bool loopSound;
    int soundPushes;
    long soundPushBytes;
    double soundPushMaxMs;
    bool soundDone;
    const float SoundBuffer = 1f;

    /// <summary>The movie's volume 0..1, read when <see cref="Play(UsmVideo, bool)"/> starts a movie (the client sets it once before Play).
    /// 0 plays no sound.</summary>
    public float SoundVolume { get; set; }
    /// <summary>The movie's sound is playing.</summary>
    public bool SoundPlaying => soundPlayer != null && GodotObject.IsInstanceValid(soundPlayer) && soundPlayer.Playing;
    /// <summary>The sound player's volume in dB (harness), or null without one.</summary>
    public float? SoundVolumeDb => soundPlayer != null && GodotObject.IsInstanceValid(soundPlayer) ? soundPlayer.VolumeDb : null;
    /// <summary>The sound has decoded and started (or was skipped): what it is, for the live log.</summary>
    public event Action<string> SoundStarted;

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
        StartSound(video, loop);
    }

    /// <summary>The usm's audio stream (ADX) starts with the picture: a generator player fed from the decoder.</summary>
    void StartSound(UsmVideo video, bool loop)
    {
        if (!video.HasAudio) return;
        loopSound = loop;
        if (SoundVolume <= 0f) { SoundStarted?.Invoke("silent (volume 0)"); return; }
        Ms2.Audio.AdxStream stream;
        try { stream = Ms2.Audio.AdxStream.Open(video.File.Audio[0].Data); }
        catch (Exception e) { SoundStarted?.Invoke($"cannot be decoded ({e.GetType().Name}: {e.Message})"); return; }
        if (stream == null || stream.Length == 0) { SoundStarted?.Invoke("the audio is not standard ADX"); return; }
        soundStream = stream;
        soundPcm = new short[stream.Rate / 10 * stream.Channels];
        soundFrames = new Vector2[stream.Rate / 10];
        soundDone = false;
        soundPushes = 0; soundPushBytes = 0; soundPushMaxMs = 0;
        soundPlayer = new AudioStreamPlayer
        {
            Name = "MovieSound",
            Stream = new AudioStreamGenerator { MixRate = stream.Rate, BufferLength = SoundBuffer },
            VolumeDb = Mathf.LinearToDb(SoundVolume),
        };
        AddChild(soundPlayer);
        soundPlayer.Play();
        soundOut = (AudioStreamGeneratorPlayback)soundPlayer.GetStreamPlayback();
        FeedSound();
        SoundStarted?.Invoke($"ADX {stream.Rate} Hz x{stream.Channels} {stream.Seconds:F2}s streamed, volume {SoundVolume:F2}");
    }

    /// <summary>Pushes the next decoded chunks (a tenth of a second each, one reused array) while the generator has room for one. Looping
    /// movies start the stream again at its end; a one-shot movie ends its sound with a chunk of silence.</summary>
    void FeedSound()
    {
        if (soundStream == null || soundOut == null || soundDone) return;
        int ch = soundStream.Channels, chunk = soundFrames.Length;
        while (soundOut.GetFramesAvailable() >= chunk)
        {
            long alloc0 = GC.GetAllocatedBytesForCurrentThread();
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            int got = 0;
            while (got < chunk)
            {
                int n = soundStream.Read(soundPcm.AsSpan(got * ch, (chunk - got) * ch));
                if (n == 0)
                {
                    if (!loopSound || soundStream.Position == 0) { soundDone = true; break; }
                    soundStream.Rewind();
                    continue;
                }
                got += n;
            }
            for (int i = 0; i < chunk; i++)
                soundFrames[i] = i < got ? new Vector2(soundPcm[i * ch] / 32768f, soundPcm[i * ch + (ch > 1 ? 1 : 0)] / 32768f) : Vector2.Zero;
            soundOut.PushBuffer(soundFrames);
            soundPushes++;
            soundPushBytes += GC.GetAllocatedBytesForCurrentThread() - alloc0;
            soundPushMaxMs = Math.Max(soundPushMaxMs, (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            if (soundDone) break;
        }
    }

    void StopSound()
    {
        if (soundPlayer != null && GodotObject.IsInstanceValid(soundPlayer))
        {
            soundPlayer.Stop();
            soundPlayer.QueueFree();
            SoundStarted?.Invoke($"stopped: {soundPushes} pushes, {soundPushBytes / 1024.0:F0} KB allocated in total, {soundPushMaxMs:F2} ms the longest, {(soundPcm.Length * 2 + soundFrames.Length * 8) / 1024} KB of PCM held");
        }
        soundPlayer = null;
        soundOut = null;
        soundStream = null;
        soundPcm = null;
        soundFrames = null;
    }

    /// <summary>VideoPlayer.stop: ends playback and clears the picture.</summary>
    public void Stop()
    {
        StopSound();
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
        FeedSound();
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
        material.SetShaderParameter(TexCb, cbTex);
        material.SetShaderParameter(TexCr, crTex);
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
