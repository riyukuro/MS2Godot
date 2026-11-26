namespace Ms2.Ui;

/// <summary>
/// CInputKeyHandler::FUN_14116d7a0 (@0x14116d7a0, inputkeyhandler.cpp:130) with the mask 0xf every caller passes: input is
/// held back. It reads no key: the bits are 1 = the loading screen's flag (SetModifier1, from UILoadingProgress::StartProgress,
/// uiloadingprogress.cpp:88, :117; ours <see cref="FieldMoveCover"/>), 2 = a text field has the focus
/// (CUIService::IsTextFieldFocused; ours <see cref="TextFieldFocused"/>), 4 = an ensemble is played
/// (CInstrumentManager::IsEnsemble) and 8 = the typing game is on (SetModifier2, uitypinggamedialog.cpp:296).
/// Callers: the action keys' gate (CKeyTable::FUN_141431fc0, keytable.cpp:2620, for ExecuteActionKey and the quick slot
/// item keys), the own player's key reads (Unk_PCMove::FUN_141683ef0 @0x141683ef0: movement, jump, casts, pickup), the
/// quick slot icon tween (SkillSlot::FUN_14175f200, skillslot.cpp:902) and the chat window's key gate
/// (UIMessenger::FUN_141a12f30, uimessenger.cpp:1613).
/// STILL-OPEN: the ensemble and the typing game are not ported (taken as off).
/// </summary>
public static class InputBlock
{
    /// <summary>CUIService::IsTextFieldFocused; the game sets it, none by default.</summary>
    public static Func<bool> TextFieldFocused = () => false;

    /// <summary>FUN_14116d7a0(0xf): the loading screen is up or a text field has the focus.</summary>
    public static bool Active => FieldMoveCover.Up || TextFieldFocused();

    /// <summary>The own pc's PCBase m_unk_158 (AddStateFlags), 0 without a pc; the game sets it.</summary>
    public static Func<uint> OwnStateFlags = () => 0;

    /// <summary>
    /// What Unk_PCMove::FUN_141683ef0 (@0x141683ef0) asks before every own key read (move, jump, casts, pickup, the attack key):
    /// input held (<see cref="Active"/>) or PCBase::vfunc_63 is 0, a state flag held (the move channel of s_keyInput, flag
    /// 0x20, <see cref="KeyInputLayers"/>; the auto find camera, 0x100).
    /// </summary>
    public static bool OwnKeysBlocked => Active || OwnStateFlags() != 0;

    /// <summary>
    /// CKeyTable::FUN_141431fc0 (@0x141431fc0, keytable.cpp:2620): m_nMode is not 0 (the attack channel of s_keyInput holds it at
    /// 0) and no held input. ExecuteActionKey, the quick slot item keys and FUN_14142bb10 ask it.
    /// </summary>
    public static bool ActionKeys => KeyInput.ActionKeyMode != 0 && !Active;
}
