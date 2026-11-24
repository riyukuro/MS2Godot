using Ms2.Formats.Pack;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The forced rename on character select: its box, its buttons and the login server's CharacterList 7 answer.</summary>
public class InvalidNameRenameTests
{
    [Test]
    public void TheBoxIsATypeTenNameInputWithTheCharacterId()
    {
        // FUN_1418f13f0 / OnLoginToGame 0x31: CreateMessageBox(10, s_char_rename_input, 0, changeInvalidCharName),
        // Append<__int64>(id), SetInput(2, "", 12, 0, 0)
        var d = InvalidNameRename.Create(1234567890123L, k => k == "s_char_rename_input" ? "Enter a new name." : null);
        Assert.That(d.Type, Is.EqualTo(10));
        Assert.That(d.Msg, Is.EqualTo("Enter a new name."));
        Assert.That(d.Extra, Is.EqualTo(InvalidNameRename.Tag));
        Assert.That(d.ArgLong(0, 0), Is.EqualTo(1234567890123L));
    }

    [Test]
    public void TheButtonsCheckTheNameThenSendOrCancel()
    {
        string Common(int id) => id == 0xd ? "Enter a name." : "";
        string Error(string n) => n == "bad" ? "This name cannot be used." : null;
        Assert.That(InvalidNameRename.Answer(0, "", Error, Common), Is.EqualTo((InvalidNameRename.Action.ShowError, "Enter a name.")));
        Assert.That(InvalidNameRename.Answer(0, "bad", Error, Common), Is.EqualTo((InvalidNameRename.Action.ShowError, "This name cannot be used.")));
        Assert.That(InvalidNameRename.Answer(0, "Good", Error, Common), Is.EqualTo((InvalidNameRename.Action.Send, (string)null)));
        Assert.That(InvalidNameRename.Answer(1, "", Error, Common).Do, Is.EqualTo(InvalidNameRename.Action.Cancel), "any other button cancels, name or not");
        Assert.That(InvalidNameRename.Answer(2, "Good", Error, Common).Do, Is.EqualTo(InvalidNameRename.Action.Cancel));
    }

    [Test]
    public void TheAnswerShowsTheClientsTextByCode()
    {
        string Common(int id) => id switch { 0x2a => "taken {0}", 0x29 => "bad word {0}", 0x2c => "system {0} {1}", _ => "" };
        Assert.That(InvalidNameRename.AnswerText(1, "New", Common), Is.Null, "1: the row takes the name");
        Assert.That(InvalidNameRename.AnswerText(2, "New", Common), Is.EqualTo("taken New"));
        Assert.That(InvalidNameRename.AnswerText(6, "New", Common), Is.EqualTo("bad word New"));
        Assert.That(InvalidNameRename.AnswerText(0, "New", Common), Is.EqualTo("system New 0"));
        Assert.That(InvalidNameRename.AnswerText(9, "New", Common), Is.EqualTo("system New 9"));
    }

    [Test]
    public void TheStartsCheckOpensTheBoxOnlyForATooLongName()
    {
        // FUN_140225210(name, reason, 1) == 2: the too long result of the 12 letter form; a banned or short name enters
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        foreach (var (label, dir) in new[] { ("MS2_DATA", d), ("MS2_GMS2_DATA", Environment.GetEnvironmentVariable("MS2_GMS2_DATA")) })
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) { TestContext.WriteLine($"{label}: not set"); continue; }
            var checker = Ms2.Formats.Text.BanTextChecker.FromPacks(new PackSet(dir), "en");
            var cases = new (string Name, bool Rename)[]
            {
                ("Abcdefghijkl", false), ("Abcdefghijklm", true), ("Abcdefghijklmnopqrstuvwx", true), ("A", false),
                ("\uAC00\uB098\uB2E4\uB77C\uB9C8\uBC14", false), ("\uAC00\uB098\uB2E4\uB77C\uB9C8\uBC14\uC0AC", true),
            };
            var wrong = cases.Where(c => (checker.CheckCharacterName(c.Name, out _, true) == InvalidNameRename.TooLong) != c.Rename).ToArray();
            TestContext.WriteLine($"{label}: {cases.Length} names, {wrong.Length} wrong: {string.Join(", ", wrong.Select(w => w.Name))}");
            Assert.That(wrong, Is.Empty, label);
            var common = new Ms2.Formats.World.GameStrings(new PackSet(dir), "en").Common();
            foreach (int id in new[] { 0xd, 0x29, 0x2a, 0x2c })
                Assert.That(common(Ms2.Formats.Text.CommonStringIds.Key(id)), Is.Not.Empty, $"{label}: common string 0x{id:x}");
            Assert.That(common("s_char_rename_input"), Is.Not.Empty, label);
            TestContext.WriteLine($"{label}: 0x2a '{common(Ms2.Formats.Text.CommonStringIds.Key(0x2a))}', 0x29 '{common(Ms2.Formats.Text.CommonStringIds.Key(0x29))}', 0x2c '{common(Ms2.Formats.Text.CommonStringIds.Key(0x2c))}'");
        }
    }
}
