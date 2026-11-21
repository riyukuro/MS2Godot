using System;
using Ms2.Net.Packets;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// What the Meret, MeretUse and CurrencyToken packets do on the client: the balances (FUN_1409304d0, SetRedMeratBalance,
/// the token globals), the system sound, the chat lines of kind 6 and 7, the reward notice and the status events
/// (CSceneStateGame::OnMeret :7025, OnMeretUse :7053, OnCurrencyToken :7094, FUN_141361460 uiworldshareinfoeventlistener.cpp:250).
/// </summary>
public sealed class CurrencyFeed(UiModel model)
{
    /// <summary>PlaySystemSound(key, "") for Merat_Get and Merat_Use.</summary>
    public Action<string> Sound;
    /// <summary>FUN_1412751b0(text, kind): a chat line of the kind (<see cref="TalkKind"/>).</summary>
    public Action<string, int> Line;
    /// <summary>UIRewardNotify::ShowMeratNotice (0x4e5d) with the gained amount.</summary>
    public Action<long> MeratNotice;
    /// <summary>UIRewardNotify::ShowMeratRedNotice (0x4e5d) with the gained amount.</summary>
    public Action<long> MeratRedNotice;
    /// <summary>NotifyChangedTokenMoney: the token pocket redraws its amounts.</summary>
    public Action TokenChanged;
    /// <summary>The NotifyChangedTokenMoney posted after the take line (scenestategame.cpp:7114): the token type and the delta
    /// (the notice's kind at +0x18 and count at +0x28); UIRewardNotify::OnNotifyChangedTokenMoney shows its toast.</summary>
    public Action<int, long> TokenMoneyNotice;
    /// <summary>Unk_SSGFormatMsg(0x2f6, 1, 1, ...): a centre notice.</summary>
    public Action<string> Notice;

    PlayerState Pc => model.Player;

    string Amount(int id, long amount) => ClientFormat.Format(model.Tables.Common(id), AchieveTexts.Group(amount));

    /// <summary>FUN_1409304d0 stores the four wallet kinds; the delta decides the sound, the line and the notice; status event 0xb.</summary>
    public void OnMeret(MeretUpdate m)
    {
        Pc.MeratWallet[0] = m.Meret; Pc.MeratWallet[1] = m.ExtraMeret; Pc.MeratWallet[2] = m.GameMeret; Pc.MeratWallet[3] = m.ExtraGameMeret;
        Pc.Merats = m.Meret + m.ExtraMeret + m.GameMeret + m.ExtraGameMeret;
        Spend(m.Delta, 0x2c6, 0x2d8, MeratNotice);
        model.Notify(StatusEvent.Merat);
    }

    /// <summary>SetRedMeratBalance; the same shape with strings 0x2c8 and 0x2da and the red notice; status event 0xd.</summary>
    public void OnMeretUse(MeretUseUpdate m)
    {
        Pc.RedMerat = m.Total;
        Spend(m.Delta, 0x2c8, 0x2da, MeratRedNotice);
        model.Notify(StatusEvent.RedMerat);
    }

    void Spend(long delta, int gainId, int useId, Action<long> notice)
    {
        if (delta > 0)
        {
            Sound?.Invoke("Merat_Get");
            Line?.Invoke(Amount(gainId, delta), TalkKind.Merat);
            notice?.Invoke(delta);
        }
        else if (delta < 0)
        {
            Sound?.Invoke("Merat_Use");
            Line?.Invoke(Amount(useId, -delta), TalkKind.Merat);
            if (model.IsFeatureEnabled(Feature.MeratMessage)) Line?.Invoke(model.Tables.Common(0x2db), TalkKind.Merat);
        }
    }

    /// <summary>The packet's token type to the currency id the token pocket and the wallet use: the globals DAT_143ab28b0..28f0
    /// take types 3 to 6 and 9 to 13 in the order ServerEnter reads the currencies 4 to 7 and 0xc to 0x10.</summary>
    public static int CurrencyOf(int type) => type switch { >= 3 and <= 6 => type + 1, >= 9 and <= 13 => type + 3, _ => 0 };

    /// <summary>The take line's string key of a token type (FUN_141361460); null for a type with none.</summary>
    static string TakeKey(int type) => type switch
    {
        6 => "s_msg_take_habi_token", 5 => "s_msg_take_lu_token", 3 => "s_msg_take_honor_token", 4 => "s_msg_take_karma_token",
        9 => "s_msg_take_reverse_coin", 10 => "s_msg_take_mentor_token", 11 => "s_msg_take_mentee_token", 12 => "s_msg_take_star_point",
        _ => null,
    };

    /// <summary>The balance is stored for the types the client's switch knows, a positive delta adds the take line (kind 7),
    /// then the token pocket is told (NotifyChangedTokenMoney); a nonzero overflow amount shows string 0x2f6 with the
    /// currency item's name (GetCurrencyItemId(type, 99)).</summary>
    public void OnCurrencyToken(CurrencyTokenUpdate m)
    {
        int currency = CurrencyOf(m.Type);
        if (currency != 0) Pc.Tokens[currency] = m.Value;
        if (m.Delta > 0)
        {
            string fmt = m.Type == 13 ? model.Tables.Common(0x2ce) : TakeKey(m.Type) is { } k ? model.Tables.Common(k) : null;
            string text = fmt == null ? "" : ClientFormat.Format(fmt, m.Delta.ToString());
            Line?.Invoke(text, TalkKind.Token);
        }
        model.Notify(StatusEvent.TokenMoney);
        TokenChanged?.Invoke();
        TokenMoneyNotice?.Invoke(m.Type, m.Delta);
        if (m.Overflow != 0)
        {
            string name = model.Tables.ItemName(TokenCurrency.ItemId(m.Type, 99)) ?? "";
            Notice?.Invoke(ClientFormat.Format(model.Tables.Common(0x2f6), name, m.Overflow.ToString()));
        }
    }
}
