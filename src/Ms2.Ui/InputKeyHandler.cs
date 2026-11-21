namespace Ms2.Ui;

/// <summary>
/// CInputKeyHandler's debug-only key test. IsKeyDown (@0x14116d950) and WasKeyPressed (@0x14116d9c0) first return false for
/// a key read with bDebugOnly while IsDebugMode (@0x14116d720) is off, then test the GM level against nLevel and ask
/// MS2InputService (IsKeyDownGated, the combo and key-group test, not modelled here). SetDebugMode (@0x14116d5d0) turns the
/// mode on only for GM level 50 and up, from the GM commands; that is not ported, so <see cref="DebugMode"/> stays off
/// unless a test or tool sets it.
/// </summary>
public static class InputKeyHandler
{
    /// <summary>CInputKeyHandler::IsDebugMode; off by default.</summary>
    public static bool DebugMode;

    /// <summary>IsKeyDown's debug gate on a key the caller read as <paramref name="held"/>: a debug-only key reads as up
    /// outside debug mode.</summary>
    public static bool IsKeyDown(bool held, bool debugOnly) => held && (!debugOnly || DebugMode);
}
