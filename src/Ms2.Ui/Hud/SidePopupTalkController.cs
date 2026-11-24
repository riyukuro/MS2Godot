namespace Ms2.Ui.Hud;

/// <summary>
/// One popup's parameters (SidePopupTalkParam, placeholder/sidepopuptalkparam.h): the Trigger type 0x13 packet fills kind,
/// movie name, illust, the duration (m_nNpcId), voice, sound and the text (OnSidePopupTalk, triggerservice.cpp:683).
/// </summary>
public sealed record SidePopupTalkParam(int Kind, string Name, string Icon, int Duration, string Sound, string Script, string Text)
{
    /// <summary>m_str68 / m_str70 / m_str78 / m_strUserValue: the table popups' map tile, map name, expire and user value
    /// (none from a trigger).</summary>
    public string MapTile { get; init; } = "";
    public string MapName { get; init; } = "";
    public string Expire { get; init; } = "";
    public string UserValue { get; init; } = "";
}

/// <summary>
/// UISidePopupTalk (client/ms2/uisidepopuptalk_1.cpp, "SidePopupTalk", UI id 0x4f57, movie uisidepopuptalk on movie layer
/// 0x1a): the NPC speech popups at the screen side. A popup shown before the movie is loaded waits, and the load is asked
/// for (<see cref="LoadRequested"/>); the load complete adds the waiting ones (<see cref="ShowPopup"/>). A kind 1 popup
/// hides the minimap (0x4e29) and the quest guide (0x4e43) until the movie's onHideComplete of kind 1.
/// STILL-OPEN: the popup table (m_table: the quest accept / progress / satisfied / complete, boss regen and map entry popups,
/// with their random scripts, CStringTable 0x44) and the boss regen whisper fallback are not ported, so only the trigger's
/// popups show; onEnterFieldWar (SidePopup send 1) and onEnterWeddingHall (0x4f96 or the wedding invitation) are logged.
/// </summary>
public sealed class SidePopupTalkController : UiController
{
    public const int UiId = 0x4f57;

    public override string Movie => "uisidepopuptalk";
    protected override string EntityMethod => "addPopup";
    protected override bool IsDialog => false;
    public override string TypeName => "SidePopupTalk";      // GetTypeName (@0x140e87260)

    /// <summary>vfunc_2 (UIHeadUpDisplay's): the host loads the movie.</summary>
    public Action LoadRequested;
    /// <summary>CReservedWordTable::ReplaceObjectAndValueWords.</summary>
    public Func<string, string> ReplaceObjectAndValueWords = s => s;
    /// <summary>CGameOption FUN_140d13c70: the sound state's mute flag and movie volume; null leaves "volume" unset.</summary>
    public Func<(bool Mute, float MovieVolume)?> SoundState;
    /// <summary>SetMinimapAndQuestGuideVisible (@0x140e886f0): the minimap and the quest guide shown or hidden.</summary>
    public Action<bool> MinimapAndQuestGuideVisible;
    /// <summary>CEffectService::PlayVoiceAt at the local player: the effect id, 0 for none.</summary>
    public Func<string, int> PlayVoice;
    /// <summary>CEffectService FUN_1411a6060.</summary>
    public Action<int> StopEffect;
    /// <summary>PlaySystemSound(name, "").</summary>
    public Action<string> SystemSound;
    /// <summary>GetServerTime, in seconds.</summary>
    public Func<long> ServerTime = () => 0;
    /// <summary>A movie call this pane reads but does not apply (see the STILL-OPEN).</summary>
    public Action<string> Unapplied;

    readonly List<SidePopupTalkParam> pending = new();      // m_pending
    readonly Dictionary<int, int> voices = new();           // m_voices: the voice key's effect

    /// <summary>The members of the last addPopup, for checks.</summary>
    public IReadOnlyDictionary<string, object> LastPopup { get; private set; }
    public int Pending => pending.Count;

    /// <summary>OnLoadComplete (@0x140e872b0): UIHeadUpDisplay's, the voices forgotten, then the waiting popups added.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        voices.Clear();
        foreach (var p in pending) AddPopup(p);
        pending.Clear();
    }

    /// <summary>vfunc_43 (@0x140e87370): UIHeadUpDisplay's, then the entity at align (5, 0, 0).</summary>
    public override void Layout(double stageW, double stageH)
    {
        base.Layout(stageW, stageH);
        if (Host.Entity != null) Host.Align(Host.Entity, 5, 0, 0);
    }

    /// <summary>ShowPopup (@0x140e879a0): without the movie the popup waits and the load is asked for, else it is added.</summary>
    public void ShowPopup(SidePopupTalkParam p)
    {
        if (Host?.Entity == null)
        {
            pending.Add(p);
            LoadRequested?.Invoke();
        }
        else AddPopup(p);
    }

    /// <summary>
    /// AddPopup (@0x140e88010): the text (a trigger's has no scripts) through ReplaceObjectAndValueWords, then addPopup with
    /// type, movie, msg, illust, duration, voice, sound, mapTile, mapName, expire, userValue and the volume (0 when muted);
    /// kind 1 hides the minimap and the quest guide.
    /// </summary>
    void AddPopup(SidePopupTalkParam p)
    {
        string msg = ReplaceObjectAndValueWords(p.Text ?? "") ?? "";
        var members = new Dictionary<string, object>
        {
            ["type"] = p.Kind,
            ["movie"] = p.Name ?? "",
            ["msg"] = msg,
            ["illust"] = p.Icon ?? "",
            ["duration"] = p.Duration,
            ["voice"] = p.Sound ?? "",
            ["sound"] = p.Script ?? "",
            ["mapTile"] = p.MapTile,
            ["mapName"] = p.MapName,
            ["expire"] = p.Expire,
            ["userValue"] = p.UserValue,
        };
        if (SoundState?.Invoke() is { } s) members["volume"] = (double)(s.Mute ? 0f : s.MovieVolume);
        LastPopup = members;
        var obj = Host.Obj(members.Select(kv => (kv.Key, kv.Value)).ToArray());
        Host.Invoke(Host.Entity, "addPopup", obj);
        if (p.Kind == 1) MinimapAndQuestGuideVisible?.Invoke(false);
    }

    /// <summary>vfunc_28 (@0x140e87420): onHideComplete, onPlayVoice, onPlaySound and the two enter buttons.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (name.Equals("onHideComplete", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length > 0 && Arg.Int(args, 0) == 1) MinimapAndQuestGuideVisible?.Invoke(true);
            return true;
        }
        if (name.Equals("onPlayVoice", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length > 0)
            {
                int key = Arg.Int(args, 0);
                if (voices.TryGetValue(key, out int effect))
                {
                    StopEffect?.Invoke(effect);
                    voices[key] = 0;
                }
                string voice = Arg.Str(args, 1);
                if (voice.Length != 0 && PlayVoice != null) voices[key] = PlayVoice(voice);
            }
            return true;
        }
        if (name.Equals("onPlaySound", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length > 0) SystemSound?.Invoke(Arg.Str(args, 1));
            return false;          // the client's branch falls through to return 0
        }
        if (name.Equals("onEnterFieldWar", StringComparison.OrdinalIgnoreCase) || name.Equals("onEnterWeddingHall", StringComparison.OrdinalIgnoreCase))
        {
            Unapplied?.Invoke(name);
            return false;
        }
        return false;
    }

    /// <summary>vfunc_45 (@0x140e87f50): getExpireDuration, the expire time (a number text) less the server time.</summary>
    public override object GetUserValue(string name, object[] args)
    {
        if (name != "getExpireDuration") return null;
        long.TryParse(Arg.Str(args, 0), out long expire);
        return (int)(expire - ServerTime());
    }

    /// <summary>vfunc_3 (@0x140e878f0): UIHeadUpDisplay's unload, the minimap and quest guide shown, every voice stopped.</summary>
    public void Unload()
    {
        MinimapAndQuestGuideVisible?.Invoke(true);
        foreach (var e in voices.Values) StopEffect?.Invoke(e);
        voices.Clear();
    }

    static class Arg
    {
        public static int Int(object[] a, int i) => i < a.Length ? (int)Avm2.Rt.ToNumber(a[i]) : 0;
        public static string Str(object[] a, int i) => i < a.Length && a[i] != null ? Avm2.Rt.ToString(a[i]) : "";
    }
}
