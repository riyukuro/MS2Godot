using Godot;

namespace Ms2Godot.World;

/// <summary>
/// WorldService's tick runs the markers' Update(GetTickCountMs2()) before the actor loop (worldservice_1.cpp:9648-9655, then
/// FUN_14123bb30 for each actor, gameobject.cpp:307-312): the moving platforms are in this frame's place, box included,
/// before PCBase::vfunc_68 reads their Position for the carry (pcbase.cpp:1797-1817). This node steps the field's markers
/// ahead of the player (ProcessPriority -1); the later per-frame callers (RemoteLayer, Field) find the frame done.
/// </summary>
public sealed partial class MarkerStep : Node
{
    public Ms2.Ui.BreakableObjects Markers;
    /// <summary>The live session's markers run on the server tick (GetServerTickAt), the offline ones on the local clock.</summary>
    public bool Live;

    public MarkerStep() { ProcessPriority = -2; }

    public override void _Process(double delta)
    {
        if (Markers == null) return;
        uint now = (uint)Time.GetTicksMsec();
        int tick = Live ? unchecked((int)(uint)(Net.LiveSession.Instance?.ServerTick ?? 0)) : unchecked((int)now);
        Markers.UpdateOnce(Engine.GetProcessFrames(), now, tick);
    }
}
