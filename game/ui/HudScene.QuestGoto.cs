using Godot;
using Ms2.Quest;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The quest windows' go-to buttons (CQuestManager::SendQuestMode12 / 13 / 19, FUN_1416cf870, FUN_1416cf980) and the story
/// quest's dispatch box (CStoryQuest::FUN_140d73960, answered by SendQuestMode20).
/// </summary>
public partial class HudScene
{
    readonly QuestButtonInputs questInputs = new();

    /// <summary>
    /// The button rules' inputs, read when a button list is built or a button is pressed: this field, and from the live
    /// session the entry of the field (OnRequestFieldEnter, scenestategame.cpp:2341-2343: the service type, the field kind
    /// and the instance id) and the party's match search.
    /// </summary>
    QuestButtonInputs QuestInputs()
    {
        var i = questInputs;
        int field = xblockFieldId;
        i.FieldId = field;
        i.LocalPlayerAlive = true;
        i.HasFieldData = true;
        i.DirectQuestFindParty = Ms2.Ui.LobbyFeatures.IsEnabled(assets.Packs, Ms2.Ui.FeatureTable.Names[0xdc - 1]);
        if (i.FieldExists == null || questInputsField != field) { questInputsField = field; i.FieldExists = f => f == field; }
        var live = Ms2Godot.Net.LiveSession.Instance;
        var entry = live?.CurrentEntry;
        i.ServiceType = entry?.FieldType ?? 0;
        i.FieldKind = entry?.InstanceType ?? 0;
        i.InstanceId = entry?.InstanceId ?? 0;
        if (live != null) i.IsMatchSearching = questMatchSearching ??= bit => live.Party.IsMatchSearching(bit);
        return i;
    }

    int questInputsField = -1;
    Func<int, bool> questMatchSearching;

    /// <summary>The go-to requests of a quest window over its quest state.</summary>
    QuestGotoRequests QuestGoto(Func<IQuestState> state) => new()
    {
        Data = id => questTable?.Quest(id),
        State = state,
        Inputs = QuestInputs,
        Meta = () => fieldMeta ??= QuestMapGraph.SharedMeta(assets.Packs),
        Rooms = () => dungeonRooms ??= DungeonRooms.Load(assets.Packs, e => Ms2.Ui.FeatureElements.IsAvailable(assets.Packs, e)),
        Send = SendQuestRequest,
        StartPopupQuestTalk = q => World.NpcTalk.Current?.StartPopupQuestTalk(q),
        TalkWithoutNpc = (q, npc) =>
        {
            GD.Print($"quest {q}: the remote complete talk without an npc (BeginTalkWithoutNpc, npc {npc}) is not ported");
            return false;
        },
    };

    /// <summary>Sends a go-to or dispatch request; with MS2_LIVE_PACKETS=1 its bytes are printed (one line per press).</summary>
    static void SendQuestRequest(Ms2.Net.Wire.IOutPacket p)
    {
        if (Ms2Godot.Net.LiveSession.LogPackets)
        {
            var w = new Maple2.PacketLib.Tools.ByteWriter();
            p.Write(w);
            GD.Print($"quest request sent: {p.Op} {Convert.ToHexString(w.Buffer, 0, w.Length)}");
        }
        Ms2Godot.Net.LiveSession.Instance?.SendTalk(p);
    }

    // ---- the dispatch box ----------------------------------------------------------------------------------------------

    QuestDispatchBoxes questDispatch;
    bool questDispatchArmed;
    Action unwatchQuestDispatch;

    /// <summary>
    /// The quest packets that set a story quest's m_7c (DecodeStart, Complete) on the live quest book. The box waits for the
    /// next one-second tick of CQuestManager::UpdateQuests; a timer runs only while one is pending.
    /// </summary>
    void WatchQuestDispatch()
    {
        if (unwatchQuestDispatch != null || Ms2Godot.Net.LiveSession.Instance is not { } live) return;
        var tables = hudModel.Tables;
        questDispatch ??= new QuestDispatchBoxes
        {
            Data = id =>
            {
                try { return World.NpcTalk.ClientQuests(assets.Packs).Quest(id); }
                catch (Exception) { return null; }
            },
            Script = tables.QuestDispatchScript,
            MapName = id => tables.MapName(id) ?? "",
            ReplaceValueWords = t => tables.ReservedWords.ReplaceValueWords(t, hudModel.HasLocalPlayer ? hudModel.Player.Name : null),
            PlayerName = () => hudModel.HasLocalPlayer ? hudModel.Player.Name : "",
            Send = SendQuestRequest,
        };
        var d = questDispatch;
        Action<int> started = id => { d.OnStarted(id); ArmQuestDispatch(); }, rewarded = id => { d.OnRewarded(id); ArmQuestDispatch(); },
                    removed = d.OnRemoved;
        Action reloaded = d.Clear;
        live.Quests.Started += started;
        live.Quests.Rewarded += rewarded;
        live.Quests.Removed += removed;
        live.Quests.Reloaded += reloaded;
        unwatchQuestDispatch = () =>
        {
            live.Quests.Started -= started;
            live.Quests.Rewarded -= rewarded;
            live.Quests.Removed -= removed;
            live.Quests.Reloaded -= reloaded;
        };
    }

    void UnwatchQuestDispatch()
    {
        unwatchQuestDispatch?.Invoke();
        unwatchQuestDispatch = null;
    }

    void ArmQuestDispatch()
    {
        if (questDispatchArmed || questDispatch is not { Pending: true } || !IsInsideTree()) return;
        questDispatchArmed = true;
        GetTree().CreateTimer(1.0).Timeout += QuestDispatchTick;
    }

    void QuestDispatchTick()
    {
        questDispatchArmed = false;
        if (!IsInstanceValid(this) || questDispatch == null) return;
        // the sub-scene 0x620001d check: a field is shown, not its loading
        int shown = questDispatch.Tick(fieldScene != null && IsInstanceValid(fieldScene));
        if (shown > 0) GD.Print($"quest dispatch: {shown} box(es) shown");
        ArmQuestDispatch();
    }
}
