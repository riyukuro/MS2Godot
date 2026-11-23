using System.Collections.Generic;

namespace Ms2.Ui.Guide;

/// <summary>
/// StateTriggerEmotionData (@0x1417c4370, stateemotionidle.cpp:357-363): the sequence names, the mode (0 plays the list once,
/// else the first name again until the duration has passed), the flag (weapons in the hands while the state runs) and the
/// value (the duration in ms). The ctor's loop flag stays false: neither trigger reader sets it.
/// <see cref="Force"/> is the reader's bForce argument to vfunc_59 (sub 7 passes 0, sub 8 passes 1).
/// </summary>
public sealed record TriggerEmotionData(IReadOnlyList<string> Emotions, int Mode, int Flag, int Value, bool Force);

/// <summary>
/// The own pc's StateTriggerEmotion (state 0x35, stateemotionidle.cpp:369-463, registered at pc.cpp:708) without its owner:
/// what it plays and when it goes back to StateIdle. The player calls <see cref="Enter"/> from vfunc_4, <see cref="Leave"/>
/// from vfunc_5 and <see cref="EndOfSequence"/> when the sequence it played has ended.
/// </summary>
public sealed class TriggerEmotion
{
    readonly List<string> emotions = new();   // m_aEmotions
    int index;                                // m_nIndex
    int mode;                                 // m_nMode
    int duration;                             // m_nDuration (ms)
    long start;                               // m_nStart (GetTickCountMs2 at entry)

    /// <summary>m_nFlag: vfunc_16 (@0x1417c47b0), which makes PC::FUN_141659000 put the weapons in the hands (pc.cpp:2496).</summary>
    public int Flag { get; private set; }

    /// <summary>
    /// vfunc_4 (@0x1417c4500, stateemotionidle.cpp:384-416): the list cleared and the index 0; the data's mode, duration,
    /// names and flag copied. Returns the first name to play (PlayAnimation(name, 1.0, m_bLoop, false)), or null with no
    /// names. The held item (SendRequestCube) and the weapons (FUN_141657c80(true) when the flag is set) are the caller's.
    /// </summary>
    public string Enter(TriggerEmotionData data, long nowMs)
    {
        emotions.Clear();
        index = 0;
        if (data != null)
        {
            mode = data.Mode;
            duration = data.Value;
            emotions.AddRange(data.Emotions);
            Flag = data.Flag;
        }
        start = nowMs;
        return emotions.Count != 0 ? emotions[0] : null;
    }

    /// <summary>vfunc_5 (@0x1417c4770, :419-423): m_nFlag = 0.</summary>
    public void Leave() => Flag = 0;

    /// <summary>
    /// EndOfSequence (@0x1417c47d0, :431-460). With a mode: past start + duration (an unsigned compare) back to StateIdle,
    /// else the first name again. Without one: the next name, back to StateIdle past the last. Returns the name to play,
    /// or null to go back to StateIdle (vfunc_59(1)); with a mode and no names it returns "" (nothing played, the state
    /// stays).
    /// </summary>
    public string EndOfSequence(long nowMs)
    {
        if (mode != 0)
        {
            if ((uint)nowMs >= (uint)(start + duration)) return null;
            return emotions.Count != 0 ? emotions[0] : "";
        }
        index++;
        return index >= emotions.Count ? null : emotions[index];
    }
}
