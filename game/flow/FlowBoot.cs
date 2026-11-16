using Godot;

namespace Ms2Godot.Flow;

/// <summary>The project's main scene: starts the persistent <see cref="GameFlow"/> beside the scene tree's current scene and leaves.</summary>
public partial class FlowBoot : Node
{
    public override void _Ready() => GameFlow.Start(GetTree());
}
