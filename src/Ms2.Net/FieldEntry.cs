using Ms2.Net.Packets;

namespace Ms2.Net;

/// <summary>
/// The field entry the server asked for (RequestFieldEnter) and its answer. CSceneStateGame::OnRequestFieldEnter
/// @0x14170f930 (scenestategame.cpp:2335) handles the packet the same way at login and in game: on error 0 the globals take
/// the new map, field type, instance and dungeon, and WorldService::LoadMap(map, 1, nPortal) tears the old field down and
/// loads the new one, keeping nPortal (m_2c0, worldservice_1.cpp:3319); the client answers ResponseFieldEnter with that
/// portal (FUN_141bd98d0) as below. A non-zero error only shows a message box
/// (GetFieldMoveErrorMessage); the current field stays.
/// <para>At the end of the field load (WorldService FUN_141bc0a70 @0x141bc0a70, worldservice_1.cpp:3779: state 2, loading,
/// becomes 4, loaded, and FUN_141bcda90 runs, :6919-6920) every field-entered callback gets the map id; CSceneStateGame's
/// (vfunc_1 @0x14170d9f0, scenestategame.cpp:1991-2011) sends RequestLoadUgcMap then Ugc 0x12, for every field and with no
/// other condition, once per load (<see cref="FieldLoaded"/>). ResponseFieldEnter has two senders only, both after that:
/// OnLoadUgcMap mode 0 (scenestategame.cpp:2283-2286, <see cref="LoadUgcMap"/>) and the drain of the cubes waiting for
/// their entities (CubeMapInformationService FUN_1410cf5c0, cubemapinformationservice_3.cpp:5701-5705,
/// <see cref="CubesMade"/>). There is no timeout and no fallback: a server that never sends LoadUgcMap gets no answer
/// (unless a cube is made in the loaded field while the timer flag is still clear, as the drain allows).</para>
/// </summary>
public sealed class FieldEntry
{
    /// <summary>The entry the client is in or loading; null before the first.</summary>
    public RequestFieldEnter Current { get; private set; }
    /// <summary>How many entries were taken (the login's is the first).</summary>
    public int Entries { get; private set; }
    /// <summary>True once the current entry has been answered.</summary>
    public bool Answered { get; private set; }
    /// <summary>True while the entry's field loads (WorldService state 2, set by LoadMap's FUN_141bbc1d0,
    /// worldservice_1.cpp:3388).</summary>
    public bool Loading { get; private set; }
    /// <summary>True once the field has loaded (state 4, FUN_141bc0a70): LoadUgcMap is read only then.</summary>
    public bool Loaded { get; private set; }
    /// <summary>The portal LoadMap keeps (m_2c0, worldservice_1.cpp:3319; FUN_141bd98d0 returns it): RequestLoadUgcMap and
    /// ResponseFieldEnter carry it.</summary>
    public int Portal { get; private set; }
    /// <summary>CubeMapInformationService's f248 (SetTimerEnabled @0x1410cb8b0, cubemapinformationservice_3.cpp:1137; read
    /// by FUN_1410cb900): set by a LoadUgcMap of mode 0 or by the cube drain, cleared by one of another mode. It starts
    /// false and no field load resets it.</summary>
    public bool TimerEnabled { get; private set; }

    /// <summary>What OnLoadUgcMap stores in CubeMapInformationService for this field (<see cref="CubeMapInfo"/>): set by every
    /// LoadUgcMap in the loaded field, cleared by a new field load (<see cref="Enter"/>).</summary>
    public CubeMapInfo CubeMap { get; } = new();

    /// <summary>The service's other values go with the cube map: FUN_1410b76a0 (ClearCubes, then FUN_1410d0310, whose reset covers
    /// the home option rows, cubemapinformationservice_3.cpp:5455-5475). Raised at each field teardown, after <see cref="CubeMap"/> is cleared.</summary>
    public event Action CubeServiceCleared;

    public FieldEntry(RequestFieldEnter first = null) { if (first != null) Enter(first); }

    /// <summary>True when the last taken entry was the map still loading (<see cref="Enter"/>): its load goes on.</summary>
    public bool LoadingAlready { get; private set; }

    /// <summary>OnRequestFieldEnter: true when the entry is taken (error 0). False on an error: nothing changes.
    /// <para>LoadMap (@0x141bbbe40, worldservice_1.cpp:3296-3297) returns at once when the field is still loading
    /// (state 2) and the map is the one in load: the entry's values are taken (the globals, scenestategame.cpp:2340-2344),
    /// but the portal stays the first entry's (m_2c0 is set only after the check, :3319), no new load starts and the
    /// load's sends go out once (<see cref="LoadingAlready"/>). Otherwise LoadMap keeps the portal and a new field load
    /// starts (state 2, FUN_141bbc1d0, :3388); a loaded field entered again for its own map loads it again.</para></summary>
    public bool Enter(RequestFieldEnter f)
    {
        if (f == null || f.Error != 0) return false;
        LoadingAlready = Loading && Current != null && Current.MapId == f.MapId;
        Current = f;
        Entries++;
        if (LoadingAlready) return true;
        Portal = f.Portal;
        Answered = false;
        Loading = true;
        Loaded = false;
        CubeMap.Reset(); CubeServiceCleared?.Invoke();   // the old field's teardown (FUN_141bbc580, worldservice_1.cpp:10015, FUN_1410b76a0 to FUN_1410d0310)
        return true;
    }

    /// <summary>The end of the field load (FUN_141bc0a70 with a loading state, then vfunc_1 :1995-2001): the state becomes
    /// loaded and RequestLoadUgcMap(portal), then Ugc 0x12 with the map id go out through <paramref name="send"/>. Only once
    /// per entry: a field not loading (none taken, or loaded already) sends nothing. Returns true when they went out.
    /// STILL-OPEN: the rest of vfunc_1 (CUgcManager::CacheBannerObjectTextures, the taxi stations' effect refresh
    /// FUN_141739450(false) and the reset of DAT_143ab285c and DAT_143ab284b when DAT_143ab2860 is 3).</summary>
    public bool FieldLoaded(Action<Wire.IOutPacket> send)
    {
        if (!Loading) return false;
        Loading = false;
        Loaded = true;
        send(new RequestLoadUgcMap(Portal));
        send(new UgcLoadBannersRequest(Current.MapId));
        return true;
    }

    /// <summary>The field teardown of a GameToGame (OnGameToGame, networkservice.cpp:953, to WorldService FUN_141bbc580
    /// @0x141bbc580): the state becomes 6 (worldservice_1.cpp:9975) whatever it was, so the load in progress ends with no
    /// field-loaded sends and the next server's entry starts a fresh load, even for the map that was loading; the cube map
    /// values go (FUN_1410b76a0, :10015). The entry and its portal stay until the next one is taken.</summary>
    public void TornDown()
    {
        Loading = false;
        Loaded = false;
        CubeMap.Reset(); CubeServiceCleared?.Invoke();
    }

    /// <summary>OnLoadUgcMap (scenestategame.cpp:2172-2331): nothing unless the field has loaded (:2174). Mode 0 sets the
    /// timer flag and answers ResponseFieldEnter(portal) through <paramref name="send"/> (:2283-2286), each time such a packet
    /// comes (the client keeps no once flag; FUN_141bdb690 after it is empty); another mode clears the flag and waits for the
    /// cubes (<see cref="CubesMade"/>). Returns the answer when one went out. STILL-OPEN: the launch option
    /// IsSkipLoadingUgcCube (:2179, mode forced to 0), which ours does not have.</summary>
    public ResponseFieldEnter LoadUgcMap(LoadUgcMap m, Action<Wire.IOutPacket> send)
    {
        if (m == null || !Loaded) return null;
        CubeMap.Apply(m);   // both modes call the same setters (:2248-2281, :2294-2327); SetTimerEnabled is among them
        TimerEnabled = m.Mode == 0;
        return m.Mode == 0 ? Answer(send) : null;
    }

    /// <summary>FUN_1410cf5c0 (cubemapinformationservice_3.cpp:5645-5716): when the last cube waiting for its entity is made
    /// and the timer flag is clear, the flag is set and ResponseFieldEnter(portal) goes out. The drain has no world-state
    /// check (:5701); ours tests <see cref="Loaded"/> because its cubes come only from packets read in the loaded field. Ours
    /// makes a packet's cubes at once, so <paramref name="made"/> cubes made by one packet drain the set together.
    /// STILL-OPEN: the client makes the entities over later frames, so cubes of several packets can drain as one set; ours
    /// answers after the first packet that makes any. Returns the answer when one went out.</summary>
    public ResponseFieldEnter CubesMade(int made, Action<Wire.IOutPacket> send)
    {
        if (made <= 0 || !Loaded || TimerEnabled) return null;
        TimerEnabled = true;
        return Answer(send);
    }

    ResponseFieldEnter Answer(Action<Wire.IOutPacket> send)
    {
        var answer = new ResponseFieldEnter(Portal);
        Answered = true;
        send(answer);
        return answer;
    }
}
