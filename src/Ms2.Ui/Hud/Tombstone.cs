namespace Ms2.Ui.Hud;

/// <summary>
/// A dead PC's tombstone (PC m_spFC8, a CBreakableObject built by PC::FUN_14165cac0, pc.cpp:4037) without its model: the
/// tomb states of CBreakableObject (breakableobject.cpp) and the hits of the server's Tombstone packet (PC::FUN_14165d300).
/// <list type="bullet">
/// <item>Setup (FUN_140adf370 without the tomb gem, then FUN_140ae0c20 and FUN_140adf9d0): the regen list from
/// SpawnAnimation, the damage list from HitAnimation, the revive list from reactionSequenceName (each split on ","), the
/// idle from InitialSequence; the entity's InitialSequence becomes a random regen entry (<see cref="Initial"/>), which is
/// what the model shows first. No SetState runs: the state is the ctor's 2 (idle) without a timer.</item>
/// <item>SetState (@0x140ade420): the state timer is reset and started for a delay above 0; 5 plays a random regen entry,
/// 2 the idle, 6 a random damage entry, 3 a random revive entry (a name the model lacks plays nothing: PlayAnimation
/// finds no sequence id). 4 hides it.</item>
/// <item>Update (@0x140adeaf0): an expired state timer runs FUN_140adf6e0: 6 and 5 go to 2, 3 marks the tomb finished
/// (m_n184, read by FUN_140adf350).</item>
/// <item>The packet (FUN_14165d300): kind 0 breaks it (FUN_14165d550(1): SetState(3, 1000), the entity's IsVisible
/// 0xa0b6b179 cleared, the bar hidden); another kind is a hit: SetState(6, 1000) and the bar's stage
/// (<see cref="GaugeStage"/>) for the kind and value as hit and max.</item>
/// <item>The PC's frame (PC::vfunc_142, pc.cpp:4612): the tomb is dropped once finished, or while it is not breaking and
/// the PC left StateDead (<see cref="KeepFor"/>).</item>
/// </list>
/// The animation service's EndOfSequence callback (FUN_140adf6e0 again) is registered only by vfunc_1, which the tomb
/// path does not run, so only the timers move the states.
/// </summary>
public sealed class Tombstone
{
    public const int Idle = 2, Break = 3, Hidden = 4, Regen = 5, Hit = 6;

    readonly string[] regen, damage, revive;
    readonly string idle;
    readonly Func<int, int> pick;
    long timerEnd = -1;

    static long nextEntity;

    /// <summary>A new entity id: the tombs and the field's breakable markers share the counter, as entity ids are unique.</summary>
    internal static ulong NextEntityId() => (ulong)Interlocked.Increment(ref nextEntity);

    /// <summary>The entity's id (GetEntityIDValue): one per tomb built; a tag area keeps the ids it hit.</summary>
    public ulong EntityId { get; } = NextEntityId();
    /// <summary>m_bHittable: "Enabled" (0x2683e9ec), cleared when the size is zero (FUN_140adf9d0, breakableobject.cpp:586-590).</summary>
    public bool Hittable { get; private set; }
    /// <summary>"ShapeDimensions" (0x9dc22aea, m_ptSize): the box (width, depth, height).</summary>
    public System.Numerics.Vector3 Size { get; private set; }
    /// <summary>m_bC1: "reactionArrow" (0x54adb2fd), read by FUN_140adf310: a tag area may hit it.</summary>
    public bool ReactionArrow { get; private set; }
    /// <summary>m_nHitSound: "SoundMaterial" (0x8843d885), the material PlayHitSound plays.</summary>
    public int HitSound { get; private set; }

    /// <summary>
    /// FUN_140adf9d0's reads (breakableobject.cpp:584) of the flat's properties: Enabled, ShapeDimensions, reactionArrow,
    /// SoundMaterial. A zero size is not hittable.
    /// </summary>
    public void Breakable(bool enabled, System.Numerics.Vector3 size, bool reactionArrow, int hitSound)
    {
        Size = size; Hittable = enabled && size != System.Numerics.Vector3.Zero; ReactionArrow = reactionArrow; HitSound = hitSound;
    }

    /// <summary>A flat's "x, y, z" value (ShapeDimensions); zero when it is missing or malformed.</summary>
    public static System.Numerics.Vector3 ParseVector(string s)
    {
        var p = (s ?? "").Split(',');
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        return p.Length == 3 && float.TryParse(p[0], System.Globalization.NumberStyles.Float, ci, out var x)
            && float.TryParse(p[1], System.Globalization.NumberStyles.Float, ci, out var y)
            && float.TryParse(p[2], System.Globalization.NumberStyles.Float, ci, out var z) ? new(x, y, z) : System.Numerics.Vector3.Zero;
    }

    /// <summary>FUN_140addea0 (breakableobject.cpp:205) for a tomb (IsTombStone: the hit timer is not read): hittable and idle.</summary>
    public bool CanBeHit => Hittable && State == Idle;

    /// <summary>
    /// GetBox (breakableobject.cpp:225): the CBoxCollision FUN_140adf9d0 builds (:624-631), size (width, depth / 2, height) and
    /// offset (0, depth / 2, 0), placed at the tomb's position and rotation (Euler degrees).
    /// </summary>
    public Ms2.Ui.Skills.HitBox Box(System.Numerics.Vector3 pos, System.Numerics.Vector3 rotDegrees) =>
        Ms2.Ui.Skills.HitBox.Pose(new(Size.X, Size.Y * 0.5f, Size.Z), new(0, Size.Y * 0.5f, 0), pos, rotDegrees);

    /// <summary>m_nState.</summary>
    public int State { get; private set; } = Idle;
    /// <summary>m_n184: the break's timer expired; the PC drops the tomb.</summary>
    public bool Finished { get; private set; }
    /// <summary>The entity's IsVisible (0xa0b6b179): cleared by the break.</summary>
    public bool Visible { get; private set; } = true;
    /// <summary>The sequence the setup gave the entity as its InitialSequence (a random regen entry, else the idle).</summary>
    public string Initial { get; }
    /// <summary>The sequence the last SetState asked for, and PlayAnimation's force flag (false for the revive entry);
    /// null when the state plays nothing. Read and cleared by the model.</summary>
    public string Play { get; private set; }
    public bool PlayForce { get; private set; }

    /// <param name="spawn">SpawnAnimation (0x2ec0ee97).</param>
    /// <param name="hit">HitAnimation (0x487e2afd).</param>
    /// <param name="initial">InitialSequence (0x3ff293c8).</param>
    /// <param name="reaction">reactionSequenceName (0xc9385319).</param>
    /// <param name="pick">rand() % n: an index below n.</param>
    public Tombstone(string spawn, string hit, string initial, string reaction, Func<int, int> pick = null)
    {
        this.pick = pick ?? (n => Random.Shared.Next(n));
        regen = Split(spawn); damage = Split(hit); revive = Split(reaction);
        idle = initial ?? "";
        Initial = regen.Length > 0 ? regen[this.pick(regen.Length)] : idle;   // FUN_140ae0c20 (breakableobject.cpp:834-839)
    }

    static string[] Split(string s) => string.IsNullOrEmpty(s) ? Array.Empty<string>() : s.Split(',', StringSplitOptions.RemoveEmptyEntries);

    string Any(string[] list) => list.Length == 0 ? null : list[pick(list.Length)];

    /// <summary>SetState(state, delayMs) at <paramref name="nowMs"/>.</summary>
    public void SetState(int state, int delayMs, long nowMs)
    {
        State = state;
        timerEnd = delayMs > 0 ? nowMs + delayMs : -1;
        (Play, PlayForce) = state switch
        {
            Regen => (Any(regen), true),
            Idle => (idle, true),
            Hit => (Any(damage), true),
            Break => (Any(revive), false),
            _ => ((string)null, false),
        };
        if (state == Hidden) Visible = false;
    }

    /// <summary>The model took <see cref="Play"/>.</summary>
    public void Played() => Play = null;

    /// <summary>Update's state timer: FUN_140adf6e0 once it expired.</summary>
    public void Update(long nowMs)
    {
        if (timerEnd < 0 || nowMs < timerEnd) return;
        timerEnd = -1;
        switch (State)
        {
            case Hit: case Regen: SetState(Idle, 0, nowMs); break;
            case Break: Finished = true; break;
        }
    }

    /// <summary>FUN_14165d300's tomb part for kind <paramref name="kind"/> (0 breaks it); returns the bar's stage for a hit
    /// (<see cref="GaugeStage"/> of the kind and value), null when the bar keeps its frame.</summary>
    public int? Apply(int kind, int value, long nowMs)
    {
        if (kind == 0)
        {
            SetState(Break, 1000, nowMs);     // FUN_14165d550(1)
            Visible = false;
            return null;
        }
        SetState(Hit, 1000, nowMs);
        return GaugeStage(kind, value);
    }

    /// <summary>PC::vfunc_142's keep test: not finished, and breaking or the PC still in StateDead.</summary>
    public bool KeepFor(bool pcDead) => !Finished && (State == Break || pcDead);

    /// <summary>UITombStoneBar::Entity::FUN_141992ea0 (uihpbar.cpp:532): stage = ceil(5 / max(1, max) x hit) in float;
    /// the label is "hit{stage}", none from stage 5 on.</summary>
    public static int? GaugeStage(int hit, int max)
    {
        float step = 5.0f / Math.Max(1, max);
        int stage = (int)MathF.Ceiling(step * hit);
        return stage >= 5 ? null : stage;
    }
}
