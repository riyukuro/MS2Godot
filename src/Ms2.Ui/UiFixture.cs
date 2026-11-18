namespace Ms2.Ui;

/// <summary>
/// FIXTURE character values for running the HUD without a server: real-shaped numbers (a level 21 Knight at 38% exp with
/// a rest pool), not a recorded character. The tables (next level exp, job names, strings) are the client's own. Replace
/// with the ServerEnter / stat packets once a session is wired.
/// </summary>
public static class UiFixture
{
    public const string Label = "fixture character (not live data)";

    public static void Apply(UiModel m)
    {
        var p = m.Player;
        p.Name = "Fixture"; p.Title = "";
        p.Level = 21; p.SubJob = 100;                              // jobname.xml 100 = Knight
        p.HpCur = 1630; p.HpMax = 2100;
        p.SpCur = 82; p.SpMax = 100;
        p.EpCur = 100; p.EpMax = 120;
        long next = m.Tables.NextExp(p.Level);
        p.Exp = next > 0 ? next * 38 / 100 : 0;
        p.RestExp = next > 0 ? next * 12 / 100 : 0;
        p.Mesos = 1_284_530; p.Merats = 350;
        p.Channel = 3;
        m.Field.Channel = 3; m.Field.Channels = new[] { 1, 2, 3, 4, 5 };
        m.FixtureLabel = Label;
    }
}
