using System.Collections.Generic;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Wire;
using Ms2.Quest;
using Ms2.Ui;
using Num = System.Numerics;

namespace Ms2Godot.Net;

/// <summary>
/// CAutoFindTaxiStation (<see cref="AutoFindTaxiStation"/>) over the live field: the own pc's model in the field starts it
/// (MyPC::vfunc_12, mypc.cpp:1016), OnTaxi's last call arms the fly back (scenestategame.cpp:7923), each frame runs the
/// OnceCallbackTimer and the Esc test (WorldService::FUN_141bcd960's tail, worldservice_1.cpp:5743), and the field leave drops
/// it with the timer (worldservice_1.cpp:9988-9989). The camera is the field's CameraMgr (<see cref="World.TriggerCamera"/>).
/// The port loads a field's entities and the own pc's model in either order: a start that finds no stations yet runs when they
/// are attached (in the client the field's entities are in before the pc is added).
/// STILL-OPEN: PCBase state flag 0x100 (AddStateFlags / RemoveStateFlags) has no port, so the player is not held while the
/// camera is away (vfunc_63, "can act", returns 0 with it); it is only kept and logged here.
/// STILL-OPEN: the cutscene movie's Pause and Resume (UICutSceneMovie::PlayDefaultScene, uicutscenemovie.cpp:451, and
/// vfunc_53, :122): PlayDefaultScene has no port, so nothing pauses the auto-find.
/// STILL-OPEN: in a camera mode other than the follow one, FUN_1409f2430 and the yaw and pitch getters read that mode's m_50,
/// m_68 and m_6c; the trigger camera keeps no such values, so the follow mode's are used.
/// </summary>
public partial class LiveSession
{
    AutoFindTaxiStation autoFind;
    /// <summary>The auto-find (one per session, as the client's singleton).</summary>
    public AutoFindTaxiStation AutoFind => autoFind ??= new AutoFindTaxiStation(new AutoFindHost(this));
    /// <summary>The state flag 0x100 the auto-find holds on the own pc (no port: <see cref="LiveSession"/> STILL-OPEN above).</summary>
    public bool AutoFindHoldsPlayer { get; private set; }
    bool autoFindWaitsForStations, autoFindStationsIn, escapeWasDown;

    /// <summary>MyPC::vfunc_12: StartAutoFind, or once the field's stations are attached.</summary>
    void AutoFindOwnPcIn()
    {
        if (!autoFindStationsIn) { autoFindWaitsForStations = true; return; }
        AutoFind.StartAutoFind();
    }

    /// <summary>The stations are attached (<see cref="AttachInteractObjects"/>): a start that waited for them runs.</summary>
    void AutoFindStationsAttached()
    {
        autoFindStationsIn = true;
        if (!autoFindWaitsForStations) return;
        autoFindWaitsForStations = false;
        AutoFind.StartAutoFind();
    }

    /// <summary>The field leave: FUN_1409f1de0 and the OnceCallbackTimer's Clear.</summary>
    void AutoFindFieldLeave()
    {
        autoFindWaitsForStations = autoFindStationsIn = false;
        autoFind?.FieldLeave();
        AutoFindHoldsPlayer = false;
    }

    /// <summary>Each frame: the OnceCallbackTimer's pass, then FUN_1409f1cb0 with the Esc key's press.</summary>
    void TickAutoFind(double delta)
    {
        if (autoFind == null) return;
        bool down = Input.IsPhysicalKeyPressed(Key.Escape);
        bool pressed = down && !escapeWasDown;
        escapeWasDown = down;
        autoFind.Advance(delta * 1000.0);
        autoFind.EscapeTick(pressed);
    }

    sealed class AutoFindHost : IAutoFindTaxiHost
    {
        readonly LiveSession s;
        readonly List<Num.Vector3> stations = new();
        public AutoFindHost(LiveSession s) { this.s = s; }

        static PackSet Packs => PackSet.Shared(Settings.DataDir);
        static World.TriggerCamera Camera => Field.Active?.TriggerCamera;

        public bool FieldAutoFind => FieldData.FindById(Packs, FieldId)?.AutoFindTaxiStation ?? false;

        public IReadOnlyList<Num.Vector3> Stations
        {
            get
            {
                stations.Clear();
                foreach (var v in s.TaxiAction.Stations) stations.Add(new Num.Vector3((float)v.X, (float)v.Y, (float)v.Z));
                return stations;
            }
        }

        public int FieldId => s.Portals?.FieldId ?? 0;
        public bool Found(int field) => Ms2Godot.Ui.StubHudData.Instance.FoundTaxis.Contains(field);
        public bool Requested(int field)
        {
            foreach (int f in s.TaxiAction.Requested) if (f == field) return true;
            return false;
        }
        public void AddRequested(int field) => s.TaxiAction.AddRequested(field);
        public bool HomeTaxiField(int field) =>
            FieldMeta.EntryOf(Packs) != null && TaxiManager.IsHomeTaxiField(QuestMapGraph.SharedMeta(Packs), field);
        public void Send(IOutPacket packet)
        {
            s.Send(packet);
            if (LiveCheck) GD.Print($"live: auto-find taxi request (mode 5) for field {FieldId}");
        }
        public void SetPlayerStateFlag(bool on)
        {
            if (!s.TaxiPlayerReady) return;    // GetLocalPlayer() == 0
            s.AutoFindHoldsPlayer = on;
        }

        public bool ModePosition(out Num.Vector3 position)
        {
            position = default;
            if (Camera is not { } c) return false;
            position = ToNum(c.Current.Position);
            return true;
        }
        public float ModeDistance => Field.Active?.CameraDistance ?? 0f;
        public float ModeYaw => Field.Active?.CameraYaw ?? 0f;
        public float ModePitch => Field.Active?.CameraPitch ?? 0f;
        public Num.Vector3 TargetModePosition => Camera is { } c ? ToNum(c.TargetSetting.Position) : default;

        public void FlyTo(Num.Vector3 position, float seconds, int mode, bool keep)
        {
            if (Camera is not { } c) return;
            var to = c.Current;
            to.Position = new Vector3(position.X, position.Y, position.Z);
            var fly = c.MakeFlyBy(seconds, to);
            fly.Kind = 2;
            c.SetFlyBy(fly, 1, 1.0f);
            c.SetMode(mode, keep);
            if (LiveCheck) GD.Print($"live: auto-find camera to ({position.X:F0}, {position.Y:F0}, {position.Z:F0}) over {seconds:F2} s, mode {mode}");
        }

        public void SetPendingMode(int mode) => Camera?.SetPendingMode(mode);

        static Num.Vector3 ToNum(Vector3 v) => new(v.X, v.Y, v.Z);
    }
}
