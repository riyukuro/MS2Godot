using Ms2.Net.Packets;

namespace Ms2.Net;

/// <summary>
/// The field entry the server asked for (RequestFieldEnter) and its one answer. CSceneStateGame::OnRequestFieldEnter
/// @0x14170f930 (scenestategame.cpp:2335) handles the packet the same way at login and in game: on error 0 the globals take
/// the new map, field type, instance and dungeon, and WorldService::LoadMap(map, 1, nPortal) tears the old field down and
/// loads the new one, keeping nPortal (m_2c0, worldservice_1.cpp:3319). When the new map's entities are all created the
/// client answers ResponseFieldEnter with that portal (FUN_141bd98d0; OnLoadUgcMap :2284 and CubeMapInformationService
/// FUN_1410cf5c0, cubemapinformationservice_3.cpp:5703), once per entry. A non-zero error only shows a message box
/// (GetFieldMoveErrorMessage); the current field stays.
/// <para>At the end of the field load (WorldService::FUN_141bcda90, worldservice_1.cpp:6919-6920) every field-entered
/// callback gets the map id; CSceneStateGame's (vfunc_1 @0x14170d9f0, scenestategame.cpp:1991-2011) sends RequestLoadUgcMap
/// then Ugc 0x12, for every field and with no other condition, once per load (<see cref="Loaded"/>). The client sends
/// ResponseFieldEnter only after that: from the LoadUgcMap answer (OnLoadUgcMap :2284, mode 0) or once the answer's cubes
/// are all created (cubemapinformationservice_3.cpp:5701-5705). STILL-OPEN: the LoadUgcMap answer is not read, so ours
/// answers when the field is built, right after the two sends.</para>
/// </summary>
public sealed class FieldEntry
{
    /// <summary>The entry the client is in or loading; null before the first.</summary>
    public RequestFieldEnter Current { get; private set; }
    /// <summary>How many entries were taken (the login's is the first).</summary>
    public int Entries { get; private set; }
    /// <summary>True once the current entry has been answered.</summary>
    public bool Answered { get; private set; }
    /// <summary>True once the current entry's field-entered sends went out.</summary>
    public bool LoadSent { get; private set; }

    public FieldEntry(RequestFieldEnter first = null) { if (first != null) Enter(first); }

    /// <summary>OnRequestFieldEnter: true when the entry is taken (error 0): a new field load starts and waits for its answer.
    /// False on an error: nothing changes.</summary>
    public bool Enter(RequestFieldEnter f)
    {
        if (f == null || f.Error != 0) return false;
        Current = f;
        Answered = false;
        LoadSent = false;
        Entries++;
        return true;
    }

    /// <summary>The field-entered sends of the loaded entry, in the client's order: RequestLoadUgcMap(portal), then Ugc 0x12
    /// with the map id; the first time only, null after that (or with no entry). STILL-OPEN: the rest of vfunc_1 (CUgcManager::
    /// CacheBannerObjectTextures, the taxi stations' effect refresh FUN_141739450(false) and the reset of DAT_143ab285c and
    /// DAT_143ab284b when DAT_143ab2860 is 3).</summary>
    public (RequestLoadUgcMap LoadUgcMap, UgcLoadBannersRequest Banners)? Loaded()
    {
        if (Current == null || LoadSent) return null;
        LoadSent = true;
        return (new RequestLoadUgcMap(Current.Portal), new UgcLoadBannersRequest(Current.MapId));
    }

    /// <summary>What the client sends when the field has loaded, in its order, through <paramref name="send"/>: the
    /// field-entered sends (<see cref="Loaded"/>, vfunc_1 :1995-2001), then the answer (<see cref="Answer"/>). Each goes out
    /// once per entry. Returns the answer when it went out now, else null.</summary>
    public ResponseFieldEnter FieldLoaded(Action<Wire.IOutPacket> send)
    {
        if (Loaded() is { } loaded) { send(loaded.LoadUgcMap); send(loaded.Banners); }
        var answer = Answer();
        if (answer != null) send(answer);
        return answer;
    }

    /// <summary>The answer of the loaded entry: ResponseFieldEnter(portal) the first time, null after that (or with no entry).</summary>
    public ResponseFieldEnter Answer()
    {
        if (Current == null || Answered) return null;
        Answered = true;
        return new ResponseFieldEnter(Current.Portal);
    }
}
