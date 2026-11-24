using Godot;

namespace Ms2Godot;

/// <summary>The run's not-applied and not-played console lines, each printed once (<see cref="Ms2.Ui.OnceLines"/>).</summary>
static class NotApplied
{
    static readonly Ms2.Ui.OnceLines lines = new(s => GD.Print(s));

    public static void Print(string line) => lines.Print(line);
}
