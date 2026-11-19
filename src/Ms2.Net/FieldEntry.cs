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
/// </summary>
public sealed class FieldEntry
{
    /// <summary>The entry the client is in or loading; null before the first.</summary>
    public RequestFieldEnter Current { get; private set; }
    /// <summary>How many entries were taken (the login's is the first).</summary>
    public int Entries { get; private set; }
    /// <summary>True once the current entry has been answered.</summary>
    public bool Answered { get; private set; }

    public FieldEntry(RequestFieldEnter first = null) { if (first != null) Enter(first); }

    /// <summary>OnRequestFieldEnter: true when the entry is taken (error 0): a new field load starts and waits for its answer.
    /// False on an error: nothing changes.</summary>
    public bool Enter(RequestFieldEnter f)
    {
        if (f == null || f.Error != 0) return false;
        Current = f;
        Answered = false;
        Entries++;
        return true;
    }

    /// <summary>The answer of the loaded entry: ResponseFieldEnter(portal) the first time, null after that (or with no entry).</summary>
    public ResponseFieldEnter Answer()
    {
        if (Current == null || Answered) return null;
        Answered = true;
        return new ResponseFieldEnter(Current.Portal);
    }
}
