namespace Ms2.Ui.Shop;

/// <summary>The money the local player holds, by currency (uicommon.cpp).</summary>
public static class CurrencyWallet
{
    /// <summary>
    /// GetCurrencyAmount (@0x1418ff5a0, uicommon.cpp:1020): what the player holds of a currency. <paramref name="amount"/> only picks
    /// the balance of currency 10 (the red merat when it covers the amount, else the merat). The item count and the guild fund are
    /// read only for their currencies.
    /// </summary>
    public static long Amount(PlayerState pc, int currency, long amount, Func<long> itemCount, Func<long> guildFund)
    {
        switch (currency)
        {
            case 1: return pc.Mesos;
            case 2: return itemCount();
            case 3: return Merat(pc);
            case 4: case 5: case 6: case 7:
            case 0xc: case 0xd: case 0xe: case 0xf: case 0x10:
                return pc.Tokens.TryGetValue(currency, out long tok) ? tok : 0;
            case 9: return pc.RedMerat;
            case 10: return pc.RedMerat < amount ? Merat(pc) : pc.RedMerat;
            case 0xb: return guildFund();
            default: return 0;
        }
    }

    /// <summary>GetMeratBalance: the wallet's sum, else the plain merat count.</summary>
    public static long Merat(PlayerState pc)
    {
        long sum = 0;
        if (pc.MeratWallet != null) foreach (var m in pc.MeratWallet) sum += m;
        return sum > 0 ? sum : pc.Merats;
    }
}
