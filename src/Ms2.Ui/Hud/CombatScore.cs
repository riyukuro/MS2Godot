namespace Ms2.Ui.Hud;

/// <summary>
/// The character window's Attack Score and Defense Score with feature 0xdf: UIPCInfoDialog::GetOffenseScore (@0x141334890) /
/// GetDefenseScore (@0x141335290, uipcinfodialog.cpp:1431-1531) fill a struct the CLua wrappers CalePcOffenseScore
/// (@0x14067aa60, commonfiles/luautil.cpp:1544) / CalePcDefenseScore (@0x14067b230, :1588) push as the arguments of the
/// client chunk's cale_pc_OffenseScore / cale_pc_DefenseScore (Data/lua/Precompiled.m2d luapack64.o, source lines 71-198
/// and 200-249); the result is (int)Number(1), 0 when the call fails (a job with no row in the chunk's tables).
/// </summary>
public static class CombatScore
{
    /// <summary>The chunk's BAP table, by job group.</summary>
    static readonly Dictionary<long, double> Bap = new()
    {
        [1] = 1.039, [10] = 1.105, [20] = 1.354, [30] = 1.398, [40] = 0.975, [50] = 1.143, [60] = 1.364, [70] = 1.151,
        [80] = 1.114, [90] = 1.259, [100] = 1.264, [110] = 1.177, [999] = 1,
    };

    /// <summary>The chunk's nddScaleFactor table, by job group.</summary>
    static readonly Dictionary<long, double> NddScaleFactor = new()
    {
        [1] = 1, [10] = 1.248, [20] = 1.044, [30] = 0.953, [40] = 0.935, [50] = 0.981, [60] = 1, [70] = 1, [80] = 0.953,
        [90] = 1.018, [100] = 1.044, [110] = 0.953,
    };

    /// <summary>The chunk's Locale_KR; get_locale is g_locale, which CLuaContext::SetLocale (luautil.cpp:250) sets to
    /// CFeatureManager::GetLocaleName.</summary>
    const string LocaleKr = "KR";

    /// <summary>The chunk's clip_value (source lines 83-87).</summary>
    static double Clip(double v, double lo, double hi)
    {
        if (v < lo) v = lo;
        if (hi < v) v = hi;
        return v;
    }

    /// <summary>(int) of the Lua number as cvttsd2si gives it: out of range or NaN is int.MinValue.</summary>
    static int Int(double v) => double.IsNaN(v) || v >= 2147483648.0 || v <= -2147483649.0 ? int.MinValue : (int)v;

    /// <summary>
    /// cale_pc_OffenseScore of the 32 pushed arguments (<paramref name="a"/>[0] the job group); null when the call fails.
    /// </summary>
    public static double? Offense(IReadOnlyList<double> a, string locale)
    {
        double job = a[0];
        double jobRate = 1;
        if (job == 40) jobRate = 1.06;
        else if (job == 70) jobRate = 1.04;
        else if (job == 90) jobRate = 1.01;
        else if (job == 110) jobRate = 1.03;
        double kind, attack;
        if (job == 30 || job == 40 || job == 110) { kind = 2; attack = a[7]; }
        else if (job == 90) { kind = 3; attack = a[6] * 0.7 + a[7] * 0.43; }
        else { kind = 1; attack = a[6]; }
        if (!Bap.TryGetValue((long)job, out double bap)) return null;   // BAP[job] nil: the division fails
        double weapon = a[4] * 1.3 / bap;
        double rarity = a[28];
        double rate = rarity == 1 ? 0.26 : rarity == 2 ? 0.27 : rarity == 3 ? 0.283 : rarity == 4 ? 0.5 : 5 <= rarity ? 1 : 0;
        double bonus = locale == LocaleKr ? a[5] + a[29] * 0.394 * 4.96 * rate : (a[5] + a[29] * 0.394) * rate * 4.96;
        double power = (weapon + bonus) * attack;
        double pierce = 1 / (1 - Math.Min(a[8], 0.3));
        double speed = 1 + (Math.Min(a[25], 150) / 100 - 1) * 0.6;
        double kindRate = kind == 1 ? 1 + Math.Min(a[9], 0.2) * 0.9
            : kind == 2 ? 1 + Math.Min(a[10], 0.2) * 0.9
            : 1 + (Math.Min(a[9], 0.2) * 0.7 + Math.Min(a[10], 0.2) * 0.3 * 0.9);
        double accuracy = Clip((a[11] - 10) / (135 + a[11] * 0.8) * 2, 0, 1);
        double critical = a[12] * 5.3 / 180 * 0.015;
        double critRate = 1 + a[13] / 100 * critical;
        double best2 = Math.Max(a[14], a[15]);
        double best6 = Math.Max(Math.Max(Math.Max(a[16], a[17]), Math.Max(a[18], a[19])), Math.Max(a[20], a[21]));
        double damage = (1 + a[23] + a[22] * 0.85 + best2 + best6) * 1;
        double boss = 1 + a[26] * 0.3;
        double p = power * pierce * speed * kindRate * accuracy * critRate * damage * boss;
        return (10 + p / 30) * jobRate;
    }

    /// <summary>
    /// cale_pc_DefenseScore of the 20 pushed arguments (<paramref name="a"/>[0] the job group); null when the call fails.
    /// </summary>
    public static double? Defense(IReadOnlyList<double> a)
    {
        double job = a[0];
        if (a[2] == 0) return 10;
        if (!NddScaleFactor.TryGetValue((long)job, out double ndd)) return null;   // nil: the multiply fails
        if (job == 20) ndd *= 1.099;
        else if (job == 30) ndd *= 0.957;
        else if (job == 60) ndd *= 1.035;
        else if (job == 80) ndd *= 0.966;
        else if (job == 90) ndd *= 1.09;
        else if (job == 100) ndd *= 1.056;
        double hpDefense = a[1] * a[2] * 0.00229 / ndd;
        double resist = 1 + (a[3] + a[4]) / 2500;
        double evasion = (1 - 70 / (a[5] + 64) * 2) * 0.5 + 1;
        double critEvasion = 1 - 1325 / (a[6] * 2) * 0.0075;
        double reduce = (a[7] + a[8]) * 0.6;
        double other = (a[9] + a[10] + a[11] + a[12] + a[13] + a[14]) * 0.2;
        return 10 + hpDefense * resist * evasion * critEvasion * (1 + (reduce + other));
    }

    /// <summary>The wrappers' return: (int)Number(1), 0 when the call fails.</summary>
    public static long Result(double? v) => v is double d ? Int(d) : 0;

    /// <summary>
    /// GetOffenseScore's struct as CalePcOffenseScore pushes it: <paramref name="basic"/> / <paramref name="special"/> are
    /// GetBasicStat / GetSpecialStat's return (the total), <paramref name="mainHandRarity"/> PCInfo::FUN_140dbaa10 (the
    /// rarity of the layer-0 item in slot 5, 0 without one). Null when a stat is not known.
    /// </summary>
    public static double[] OffenseArgs(long job, long[] stats0, long average, Func<int, float?> basic, Func<int, bool, float?> special,
        int mainHandRarity)
    {
        var a = new double[32];
        float? Hundredth(float? f) => f * 0.01f;
        float?[] fs =
        {
            Hundredth(basic(0x1f)), Hundredth(special(0x1a, true)), Hundredth(special(0x1b, true)),
        };
        a[0] = job; a[1] = stats0[7]; a[2] = stats0[0xa];
        if (basic(0xe) is not float f18) return null;
        a[3] = f18;
        a[4] = average;
        if (basic(0x1d) is not float f28 || basic(0x17) is not float f30 || basic(0x18) is not float f38) return null;
        a[5] = (long)f28; a[6] = (long)f30; a[7] = (long)f38;
        for (int i = 0; i < 3; i++) { if (fs[i] is not float v) return null; a[8 + i] = v; }
        if (basic(0xf) is not float f50 || basic(0x11) is not float f58 || basic(0x12) is not float f60) return null;
        a[11] = (long)f50; a[12] = (long)f58; a[13] = (long)f60;
        int k = 14;
        foreach (int s in new[] { 0x18, 0x19, 0x13, 0x12, 0x17, 0x16, 0x14, 0x15, 0xc, 7, 0x10 })
        {
            if (Hundredth(special(s, true)) is not float v) return null;
            a[k++] = v;
        }
        if (basic(0xd) is not float f94 || Hundredth(special(0x23, true)) is not float f98 || special(0x36, false) is not float fa0
            || basic(0x1e) is not float fb0 || Hundredth(special(0xb1, true)) is not float fb8 || Hundredth(special(0xb2, true)) is not float fbc)
            return null;
        a[25] = f94; a[26] = f98; a[27] = (long)fa0; a[28] = mainHandRarity; a[29] = (long)fb0; a[30] = fb8; a[31] = fbc;
        return a;
    }

    /// <summary>GetDefenseScore's struct as CalePcDefenseScore pushes it (m_5c, GetSpecialStat(0xcc), is filled but not
    /// pushed). Null when a stat is not known.</summary>
    public static double[] DefenseArgs(long job, long[] stats0, Func<int, float?> basic, Func<int, bool, float?> special)
    {
        var a = new double[20];
        a[0] = job; a[1] = stats0[4];
        int k = 2;
        foreach (int s in new[] { 0x14, 0x19, 0x1a, 0x10, 0x13 })
        {
            if (basic(s) is not float v) return null;
            a[k++] = (long)v;
        }
        foreach (int s in new[] { 0x25, 0x26, 0x1d, 0x1c, 0x21, 0x20, 0x1e, 0x1f })
        {
            if (special(s, true) is not float v) return null;
            a[k++] = v * 0.01f;
        }
        if (basic(0x15) is not float f58 || special(0x22, true) is not float f60 || special(0x24, true) is not float f64
            || basic(5) is not float f68 || special(0x37, false) is not float f70)
            return null;
        a[15] = f58 * 0.01f; a[16] = f60; a[17] = f64; a[18] = (long)f68; a[19] = (long)f70;
        return a;
    }

    /// <summary>
    /// CLua::FUN_140674a70 (luautil.cpp:546, C++ not Lua): the critical damage rate GetBasicStat(0x12) shows, Clamp(1 + value /
    /// 1000 * max(1 - rate, 0), 1, 2.5), plus bonus / 1000 * max(1 - rate, 0) when a bonus is given.
    /// </summary>
    public static float CriticalDamageRate(long value, float rate, long bonus)
    {
        double remainder = 1.0 - rate;
        double result = value / 1000.0 * Math.Max(remainder, 0.0);
        result = Math.Clamp(1.0 + result, 1.0, 2.5);
        if (bonus != 0) result += bonus / 1000.0 * Math.Max(1.0 - rate, 0.0);
        return (float)result;
    }
}
