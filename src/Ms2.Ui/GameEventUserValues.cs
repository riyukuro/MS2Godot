using Ms2.Formats.Text;
using Ms2.Formats.World;
using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// The client's two GameEventUserValue tables (DAT_143ab2fc0, list 0, and DAT_143ab3010, list 1; commonfiles/gameeventuservalue.cpp)
/// that the GameEventUserValue packet fills (FUN_140930fe0 @0x140930fe0), and the GameEventUserValueManager (@0x140da5390,
/// placeholder/gameeventuservaluemanager.h) that tells listeners when one value arrives. A value is a text with an expiry
/// (server seconds); the readers give the default for a missing value or one whose expiry is not later than the server time
/// (Get @0x14041fc20; the typed readers Unk_AttendValue::GetValue @0x140961970, FUN_140c5afd0 @0x140c5afd0, FUN_1404822b0).
/// </summary>
public sealed class GameEventUserValues
{
    readonly Func<long> serverTime;
    readonly Dictionary<(int Type, int Id), (string Value, long Time)>[] lists = { new(), new() };
    readonly Dictionary<int, List<Action<GameEventVariable>>> listeners = new();

    /// <param name="serverTime">GetServerTime (server seconds).</param>
    public GameEventUserValues(Func<long> serverTime) { this.serverTime = serverTime ?? (() => 0); }

    /// <summary>The number of values held in a list (expired ones too: the client's map keeps them).</summary>
    public int Count(int list) => lists[list].Count;

    /// <summary>
    /// The packet (FUN_140930fe0): kind 0 clears the list and stores every variable (GameEventUserValue::Read @0x14041f7c0);
    /// kind 1 stores the one variable under its key (ReadVariable @0x14041f850, a repeated key replaces) and then Dispatch tells
    /// the listeners of its type (@0x140da55a0). A message the reader marked unread (List -1) changes nothing.
    /// </summary>
    public void Apply(GameEventUserValueMessage m)
    {
        if (m.List < 0) return;
        var table = lists[m.List];
        if (m.Kind == 0) table.Clear();
        foreach (var v in m.Variables)
        {
            table[(v.Type, v.Id)] = (v.Value, v.Time);
            if (m.Kind == 1) Dispatch(v);
        }
    }

    /// <summary>GameEventUserValueManager::RegisterListener @0x140da5430: a listener of a value type; one delegate is added once per call.</summary>
    public void RegisterListener(int type, Action<GameEventVariable> listener)
    {
        if (!listeners.TryGetValue(type, out var l)) listeners[type] = l = new();
        l.Add(listener);
    }

    /// <summary>GameEventUserValueManager::UnregisterListener @0x140da5490: takes one registration of the delegate off.</summary>
    public void UnregisterListener(int type, Action<GameEventVariable> listener)
    {
        if (listeners.TryGetValue(type, out var l)) l.Remove(listener);
    }

    /// <summary>GameEventUserValueManager::Dispatch @0x140da55a0: every listener of the type, in the order registered.</summary>
    void Dispatch(GameEventVariable v)
    {
        if (!listeners.TryGetValue(v.Type, out var l)) return;
        foreach (var f in l.ToArray()) f(v);
    }

    /// <summary>GameEventUserValue::Get @0x14041fc20: the value (text, expiry) while its expiry is later than the server time.</summary>
    public bool TryGet(int list, int type, int id, out string value, out long time)
    {
        if (lists[list].TryGetValue((type, id), out var e) && e.Time > serverTime())
        {
            (value, time) = e;
            return true;
        }
        value = null;
        time = 0;
        return false;
    }

    /// <summary>GameEventUserValue::Has @0x14041f9e0 (the id form; <c>Has(type)</c> @0x14041f940 is id 0).</summary>
    public bool Has(int list, int type, int id = 0) => TryGet(list, type, id, out _, out _);

    /// <summary>GameEventUserValue::GetTime @0x14041faf0: the expiry of a value that has not expired.</summary>
    public bool GetTime(int list, int type, int id, out long time) => TryGet(list, type, id, out _, out time);

    /// <summary>GameEventUserValue::GetValue&lt;int&gt; (Unk_AttendTable::GetValue @0x1409617d0 into Unk_AttendValue::GetValue
    /// @0x140961970, actionkeyobject.cpp:3097-3113): _wtoi of the text, the default when missing or expired. The expiry test
    /// here is the entry's own (<c>m_expire &lt;= now</c>), the same one Get makes.</summary>
    public int GetInt(int list, int type, int id, int dflt = 0) => TryGet(list, type, id, out var s, out _) ? ReservedWords.Atoi(s) : dflt;

    /// <summary>GetValue&lt;__int64&gt; (@0x140c5af20 into @0x140c5afd0, uileveluppackagewidgetdialog.cpp:595-618): _wtoi64 of the text.</summary>
    public long GetInt64(int list, int type, int id, long dflt = 0) => TryGet(list, type, id, out var s, out _) ? ArgumentFormat.Atoi64(s) : dflt;

    /// <summary>GetBoolValue @0x1404821d0 (gamerule.cpp:341) into FUN_1404822b0 (:325): ParseBoolW of the text (packetlogger.cpp:183:
    /// true when the text has any of 1 o O t T).</summary>
    public bool GetBool(int list, int type, int id, bool dflt = false) => TryGet(list, type, id, out var s, out _) ? ParseBool(s) : dflt;

    /// <summary>ParseBoolW (packetlogger.cpp:183): false for an empty text, else true when it holds one of "1oOtT".</summary>
    public static bool ParseBool(string s) => !string.IsNullOrEmpty(s) && s.IndexOfAny(new[] { '1', 'o', 'O', 't', 'T' }) >= 0;

    /// <summary>The text of a value that has not expired (gameeventcli.cpp reads the s_attendGift_* strings this way); null otherwise.</summary>
    public string GetString(int list, int type, int id) => TryGet(list, type, id, out var s, out _) ? s : null;
}
