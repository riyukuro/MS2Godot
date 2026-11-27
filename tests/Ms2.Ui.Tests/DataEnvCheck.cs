using Ms2.Ui.Tests;
using NUnit.Framework;

/// <summary>
/// Checks the data env once, before any Ui test runs. MS2_DATA is the KMS case and MS2_GMS2_DATA the GMS2 one. If MS2_DATA holds
/// the GMS2 data (locale NA, <see cref="Ms2.Ui.LobbyFeatures.LocaleOf"/>) and MS2_GMS2_DATA is unset, the suite uses the GMS2
/// data as the KMS set. About 150 tests then skip, and the KMS-only ones fail far from the cause; for example, NoticeTests
/// checks the GMS2 strings against the KMS list. So the run stops at the start and says why.
/// In the global namespace so it covers every fixture of the assembly (both test namespaces).
/// </summary>
[SetUpFixture]
public sealed class DataEnvCheck
{
    [OneTimeSetUp]
    public void CheckDataEnv()
    {
        var why = Problem(Environment.GetEnvironmentVariable("MS2_DATA"), Environment.GetEnvironmentVariable("MS2_GMS2_DATA"),
            d => Ms2.Ui.LobbyFeatures.LocaleOf(TestData.Packs(d)));
        if (why != null) Assert.Fail(why);
    }

    /// <summary>
    /// The reason the env is wrong, else null. Wrong means MS2_DATA is a data folder whose locale is NA (the GMS2 data) and
    /// MS2_GMS2_DATA is unset. With no MS2_DATA (the CI form) or KMS data (locale KR), the env is fine.
    /// </summary>
    public static string Problem(string data, string gms2Data, Func<string, string> localeOf)
    {
        if (string.IsNullOrEmpty(data) || !Directory.Exists(data) || !string.IsNullOrEmpty(gms2Data)) return null;
        if (!string.Equals(localeOf(data), "NA", StringComparison.OrdinalIgnoreCase)) return null;
        return "MS2_DATA is GMS2 data (locale NA) but MS2_GMS2_DATA is not set, so the suite would take it as the KMS set. "
            + "Set MS2_GMS2_DATA to the same folder, and unset MS2_KMS_DATA unless it holds KMS data.";
    }
}
