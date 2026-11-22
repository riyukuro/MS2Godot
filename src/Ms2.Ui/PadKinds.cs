namespace Ms2.Ui;

/// <summary>
/// MS2InputService's pad kind (m_nPadKind, read by FUN_140a58540()->GetPadKind()): <see cref="None"/> 0, <see cref="XInput"/> 1,
/// <see cref="Dual"/> 2, <see cref="Steam"/> 3 (never set by the client's DetectGamePad).
/// </summary>
public static class PadKinds
{
    public const int None = 0, XInput = 1, Dual = 2, Steam = 3;

    /// <summary>
    /// MS2InputService::DetectGamePad (@0x140d8be40, ms2inputservice.cpp:669-710) and SetGamePadEnabled's off branch (@0x140d8b900,
    /// :623-626): the game pad switch off gives none; with it on, the game pad feature (0xb5) off gives none, a working XInput pad
    /// (slots 0..3) gives 1, otherwise a working DirectInput8 pad (slot 4) gives 2.
    /// STILL-OPEN: Godot reports one joypad list, so which API the client would have opened the pad through is a guess made by the
    /// caller (<paramref name="xinputPad"/> / <paramref name="directInputPad"/>).
    /// </summary>
    public static int Detect(bool padEnabled, bool featureOn, bool xinputPad, bool directInputPad)
    {
        if (!padEnabled || !featureOn) return None;
        if (xinputPad) return XInput;
        return directInputPad ? Dual : None;
    }
}
