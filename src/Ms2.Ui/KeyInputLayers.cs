namespace Ms2.Ui;

/// <summary>
/// UICommon::CMyPCKeyInputController (uicommon.cpp:1540-1682), the client's one s_keyInput: three input channels the
/// dialogs hold while they are up, each a set of layers (a dialog's type id) and a count. The first layer holds the channel,
/// the last release gives it back; a layer holds it once however often it asks (FUN_141901170).
/// <list type="bullet">
/// <item>move (FUN_141901070): state flag 0x20 on the local pc (PCBase::AddStateFlags / RemoveStateFlags), so
/// PCBase::vfunc_63 is 0 and Unk_PCMove::FUN_141683ef0 (@0x141683ef0) reads no move, jump, cast or pickup key
/// (<see cref="InputBlock.OwnKeysBlocked"/>), and the move gate is shut (<see cref="MoveGate"/>);</item>
/// <item>attack (FUN_141900ff0): CKeyTable::SetMode(0) (@0x14142bad0), the action keys (dialog toggles, quick slot items) are
/// off while the mode is 0 (<see cref="InputBlock.ActionKeys"/>);</item>
/// <item>skill (FUN_141900f70): CUIService::FUN_141adbdc0, the quick slot / skill use block. STILL-OPEN: not ported, the set
/// and count are kept (<see cref="SkillBlocked"/>) and nothing reads them.</item>
/// </list>
/// The callers: UIModalDialog::vfunc_47 (@0x141a556d0, uimodaldialog.cpp:84) is FUN_141901470 (move and attack), UIObject
/// vfunc_47 (@0x14131edb0) FUN_141901420 (move and skill), UIMessageBox::vfunc_47 (@0x1419f7f00) the move and the attack
/// channel each on its own gate, and the FUN_141901310 (channels 1 move, 2 skill, 4 attack) callers.
/// STILL-OPEN: the lift mount (HasLift: the pc's construction guide gets the same flag) is not ported.
/// </summary>
public sealed class KeyInputLayers
{
    /// <summary>The state flag the move channel holds on the local pc.</summary>
    public const uint MoveStateFlag = 0x20;

    readonly HashSet<int> move = new(), skill = new(), attack = new();
    int nMove, nSkill, nAttack;

    /// <summary>m_nMode of the CKeyTable (SetMode, @0x14142bad0; 1 by default).</summary>
    public int ActionKeyMode { get; private set; } = 1;

    /// <summary>A layer holds the move channel (the local pc has state flag 0x20).</summary>
    public bool MoveHeld => nMove > 0;

    /// <summary>A layer holds the skill channel.</summary>
    public bool SkillBlocked => nSkill > 0;

    /// <summary>A layer holds the attack channel (the action keys' mode is 0).</summary>
    public bool AttackHeld => nAttack > 0;

    /// <summary>The local pc's state flags change: <c>true</c> adds <see cref="MoveStateFlag"/>, <c>false</c> removes it. The
    /// game sets it to its own pc's AddStateFlags / RemoveStateFlags; none without one (the client's pPC null).</summary>
    public Action<bool> LocalPcFlag = _ => { };

    /// <summary>FUN_141900ed0 (@0x141900ed0): the count up or down (not below 0); 0 while it holds, nDefault (1) once free.</summary>
    static int Count(bool release, ref int count, int nDefault)
    {
        count = release ? Math.Max(count - 1, 0) : count + 1;
        return count > 0 ? 0 : nDefault;
    }

    /// <summary>FUN_141901170 (@0x141901170): a release erases the layer, a block inserts it; true when the set changed.</summary>
    static bool Layer(bool release, int layer, HashSet<int> set) => release ? set.Remove(layer) : set.Add(layer);

    /// <summary>FUN_141901070 (@0x141901070): the move channel.</summary>
    public void Move(bool release, int layer)
    {
        if (!Layer(release, layer, move)) return;
        int nBlock = Count(release, ref nMove, 1);
        LocalPcFlag(nBlock == 0);
    }

    /// <summary>FUN_141900f70 (@0x141900f70): the skill channel (its effect, FUN_141adbdc0, is not ported).</summary>
    public void Skill(bool release, int layer)
    {
        if (!Layer(release, layer, skill)) return;
        Count(release, ref nSkill, 1);
    }

    /// <summary>FUN_141900ff0 (@0x141900ff0): the attack channel, CKeyTable::SetMode(nBlock).</summary>
    public void Attack(bool release, int layer)
    {
        if (!Layer(release, layer, attack)) return;
        ActionKeyMode = Count(release, ref nAttack, 1);
    }

    /// <summary>FUN_141901310 (@0x141901310): the channels of a mask, 1 move, 2 skill, 4 attack.</summary>
    public void Channels(bool release, uint channels, int layer)
    {
        if ((channels & 1) != 0) Move(release, layer);
        if ((channels & 2) != 0) Skill(release, layer);
        if ((channels & 4) != 0) Attack(release, layer);
    }

    /// <summary>FUN_141901420 (@0x141901420): move and skill (UIObject::vfunc_47).</summary>
    public void MoveAndSkill(bool release, int layer) { Move(release, layer); Skill(release, layer); }

    /// <summary>FUN_141901470 (@0x141901470): move and attack (UIModalDialog::vfunc_47).</summary>
    public void MoveAndAttack(bool release, int layer) { Move(release, layer); Attack(release, layer); }

    /// <summary>FUN_1419012a0 (@0x1419012a0): every set and count cleared. The mode and the pc's flag are left as they are,
    /// as the client's does.</summary>
    public void Clear()
    {
        move.Clear(); skill.Clear(); attack.Clear();
        nAttack = 0; nMove = 0; nSkill = 0;
    }
}

/// <summary>The client's one s_keyInput (<see cref="KeyInputLayers"/>) and its free functions (FUN_141901310 ... FUN_141901470).</summary>
public static class KeyInput
{
    public static KeyInputLayers Shared = new();

    /// <summary>CKeyTable m_nMode: 1 the action keys read, 0 a layer holds them.</summary>
    public static int ActionKeyMode => Shared.ActionKeyMode;

    /// <summary>FUN_141901390 (@0x141901390).</summary>
    public static void Move(bool release, int layer) => Shared.Move(release, layer);

    /// <summary>FUN_1419013c0 (@0x1419013c0).</summary>
    public static void Attack(bool release, int layer) => Shared.Attack(release, layer);

    /// <summary>FUN_1419013f0 (@0x1419013f0).</summary>
    public static void Skill(bool release, int layer) => Shared.Skill(release, layer);

    /// <summary>FUN_141901420 (@0x141901420).</summary>
    public static void MoveAndSkill(bool release, int layer) => Shared.MoveAndSkill(release, layer);

    /// <summary>FUN_141901470 (@0x141901470).</summary>
    public static void MoveAndAttack(bool release, int layer) => Shared.MoveAndAttack(release, layer);

    /// <summary>FUN_141901310 (@0x141901310).</summary>
    public static void Channels(bool release, uint channels, int layer) => Shared.Channels(release, channels, layer);
}
