using Ms2.Formats.Pack;

namespace Ms2Godot.Tests;

/// <summary>A field for the checks that need one without its map: no load at _Ready, no frame and no teardown; the given packs
/// stand for the loaded field's.</summary>
public partial class FieldStub : Field
{
    public FieldStub() { }
    public FieldStub(PackSet set) { packs = set; }
    public override void _Ready() { }
    public override void _Process(double delta) { }
    public override void _ExitTree() { }
}
