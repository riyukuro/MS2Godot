using System.Xml.Linq;
using Ms2.Formats.Options;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

public class GameOptionTests
{
    static XDocument Doc(string xml) => XDocument.Parse(xml);
    static GameOption Loaded(string xml, bool features = true)
    {
        var o = new GameOption(0);
        o.Load(Doc(xml), _ => features);
        return o;
    }

    [Test]
    public void ResetValuesWithoutAFile()
    {
        var o = Loaded("<ms2/>");
        Assert.That(o.CameraOption.CameraFovStepMouseMode, Is.EqualTo(0x82));
        Assert.That(o.GraphicOption.Width, Is.EqualTo(1024));
        Assert.That(o.GraphicOption.Height, Is.EqualTo(768));
        Assert.That(o.GraphicOption.ScreenMode, Is.EqualTo(1));
        Assert.That(o.SoundOption.CharSoundStep, Is.EqualTo(7));
        Assert.That(o.SoundOption.InstrumentSoundStep, Is.EqualTo(10));
        Assert.That(o.SoundOption.CategoryVolume(2), Is.EqualTo(0f));
        Assert.That(o.StatusOption.DisplayPcIllust, Is.True, "KR region");
        Assert.That(new GameOption(2).StatusOption.DisplayPcIllust, Is.False);
        Assert.That(new GameOption(2).LanguageOption.LanguageIndex, Is.EqualTo(2));
        Assert.That(GameOption.IsCurrent(Doc("<ms2/>")), Is.False);
    }

    [Test]
    public void ReadHelpersFollowTinyXml()
    {
        var o = Loaded(@"<ms2><camera bgTransparency=""TRUE"" vibrateEffect=""yes"" cameraLooseStep=""45abc"" cameraFovStep=""x""/>
            <interface UIScale=""junk"" damageEffectAlpha=""500""/><sound mainSoundStep=""-3"" bgmSoundStep="" 12""/></ms2>");
        Assert.That(o.CameraOption.BgTransparency, Is.True, "case-insensitive true");
        Assert.That(o.CameraOption.VibrateEffect, Is.True, "other text leaves the value");
        Assert.That(o.CameraOption.CameraLooseStep, Is.EqualTo(45), "sscanf %d takes the leading number");
        Assert.That(o.CameraOption.CameraFovStep, Is.EqualTo(100), "no number leaves the value");
        Assert.That(o.InterfaceOption.UIScale, Is.EqualTo(0.8f), "unparsable float stores 0, then the clamp");
        Assert.That(o.InterfaceOption.DamageEffectAlpha, Is.EqualTo(100));
        Assert.That(o.SoundOption.MainSoundStep, Is.EqualTo(0));
        Assert.That(o.SoundOption.BgmSoundStep, Is.EqualTo(10));
    }

    [Test]
    public void BattleDamageFormsAndLegacyFallback()
    {
        var o = Loaded(@"<ms2><damage giveDamage=""mine"" takeDamage=""PARTY"" otherDamageform=""alpha""/></ms2>");
        Assert.That((o.BattleOption.GiveDamage, o.BattleOption.TakeDamage, o.BattleOption.OtherDamageForm), Is.EqualTo((1, 3, 2)));
        var legacy = Loaded(@"<ms2><battle DisplayMyTakeDamage=""true"" DisplayPartyTakeDamage=""false"" DisplayPartyGiveDamage=""true""/></ms2>", features: false);
        Assert.That((legacy.BattleOption.GiveDamage, legacy.BattleOption.TakeDamage, legacy.BattleOption.OtherDamageForm), Is.EqualTo((3, 1, 3)));
        Assert.That(Loaded(@"<ms2><battle/></ms2>").BattleOption.GiveDamage, Is.EqualTo(7), "GemDamageFont on");

        var doc = Doc(@"<ms2><battle DiffSize=""true"" DisplayMyGiveDamage=""true""/></ms2>");
        o.BattleOption.OtherDamageForm = 0;
        o.BattleOption.Save(doc);
        var battle = doc.Root.Element("battle");
        Assert.That(battle.Attribute("DiffSize"), Is.Null);
        Assert.That(battle.Attribute("DisplayMyGiveDamage"), Is.Null);
        Assert.That((string)doc.Root.Element("damage").Attribute("otherDamageform"), Is.EqualTo("default"));
        Assert.That((string)doc.Root.Element("damage").Attribute("takeDamage"), Is.EqualTo("party"));
    }

    [Test]
    public void ChaosMeterNeedsTheFeature()
    {
        const string xml = @"<ms2><interface EnableChaosDamageMeter=""true""/></ms2>";
        Assert.That(Loaded(xml, true).InterfaceOption.EnableChaosDamageMeter, Is.True);
        Assert.That(Loaded(xml, false).InterfaceOption.EnableChaosDamageMeter, Is.False);
    }

    [Test]
    public void SaveKeepsTheClientShape()
    {
        var doc = Doc(@"<ms2><graphic quality=""2"" resolution=""4"" fullScreen=""2"" advancedEffect=""true""/><unknown a=""1""/></ms2>");
        var o = new GameOption(0);
        o.Load(doc, _ => true);
        o.ConnectOption.CharName = "x";
        o.ConnectOption.CharList.AddRange(new ulong[] { 5, 7 });
        o.InterfaceOption.UIScale = 1.1f;
        o.Save(doc);
        var root = doc.Root;
        Assert.That(root.Element("unknown"), Is.Not.Null, "other sections stay");
        Assert.That((string)root.Element("graphic").Attribute("fullScreen"), Is.EqualTo("2"));
        Assert.That((string)root.Element("graphic").Attribute("advancedEffect"), Is.EqualTo("true"), "read, never written");
        Assert.That((string)root.Element("interface").Attribute("UIScale"), Is.EqualTo("1.1"));
        Assert.That((string)root.Element("connect").Attribute("CharName"), Is.EqualTo("x"));
        Assert.That(o.ConnectOption.CharName, Is.EqualTo(""), "cleared once written");
        Assert.That((string)root.Element("connect").Attribute("CharList"), Is.EqualTo("5,7"));
        Assert.That(root.Element("chroma"), Is.Null, "written only when set");
        Assert.That((string)root.Element("option").Attribute("version"), Is.EqualTo("21"));
        Assert.That(root.Elements().Last().Name.LocalName, Is.EqualTo("option"));
        Assert.That(GameOption.IsCurrent(doc), Is.True);

        var again = new GameOption(0);
        again.Load(doc, _ => true);
        Assert.That(again.GraphicOption.Quality, Is.EqualTo(2));
        Assert.That(again.ConnectOption.CharList, Is.EqualTo(new ulong[] { 5, 7 }));
        Assert.That(again.InterfaceOption.UIScale, Is.EqualTo(1.1f));
    }

    [Test]
    public void TheServerListsSavedServerAndChannelGoIntoTheConnectBlock()
    {
        var doc = Doc("<ms2><connect WorldName=\"Old\" Channel=\"3\" CharSN=\"9\"/><other a=\"1\"/></ms2>");
        var o = new GameOption(0);
        o.Load(doc, _ => true);
        Assert.That((o.ConnectOption.WorldName, o.ConnectOption.ChannelNumber), Is.EqualTo(("Old", 3)));
        o.ConnectOption.SetWorldName(doc, "New");
        o.ConnectOption.SetChannel(doc, 12);
        var connect = doc.Root.Element("connect");
        Assert.That(((string)connect.Attribute("WorldName"), (string)connect.Attribute("Channel")), Is.EqualTo(("New", "12")));
        Assert.That((string)connect.Attribute("CharSN"), Is.EqualTo("9"), "only the one attribute is written");
        Assert.That(doc.Root.Element("other"), Is.Not.Null);
        var again = new GameOption(0);
        again.Load(doc, _ => true);
        Assert.That((again.ConnectOption.WorldName, again.ConnectOption.ChannelNumber), Is.EqualTo(("New", 12)));
    }

    [Test]
    public void TheEnteredCharacterGoesIntoTheConnectBlockWithItsNameCleared()
    {
        var doc = Doc("<ms2><connect CharSN=\"5\" CharName=\"Old\" Channel=\"2\"/></ms2>");
        var o = new GameOption(0);
        o.Load(doc, _ => true);
        Assert.That((o.ConnectOption.CharSN, o.ConnectOption.CharName), Is.EqualTo((5UL, "Old")));
        o.ConnectOption.SetCharSN(doc, 1234567890123UL);
        var connect = doc.Root.Element("connect");
        Assert.That(((string)connect.Attribute("CharSN"), (string)connect.Attribute("CharName")), Is.EqualTo(("1234567890123", "")));
        Assert.That((string)connect.Attribute("Channel"), Is.EqualTo("2"), "the other attributes stay");
        var again = new GameOption(0);
        again.Load(doc, _ => true);
        Assert.That((again.ConnectOption.CharSN, again.ConnectOption.CharName), Is.EqualTo((1234567890123UL, "")));
    }

    [TestCase("", 0)]
    [TestCase("7", 7)]
    [TestCase(" 4x", 4)]
    [TestCase("-2", -2)]
    [TestCase("abc", 0)]
    public void TheSavedChannelReadsAsAtoi(string text, int channel) =>
        Assert.That(new GameOption.Connect { Channel = text }.ChannelNumber, Is.EqualTo(channel));

    [Test]
    public void ShippedDefaultLoads()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        using var packs = new PackSet(data);
        var doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/default/defaultgameoption.xml")).TrimStart('﻿'));
        var o = new GameOption(0);
        o.Load(doc, _ => true);
        Assert.That((o.GraphicOption.Width, o.GraphicOption.Height, o.GraphicOption.ScreenMode, o.GraphicOption.ResolutionIndex), Is.EqualTo((1024, 768, 2, 4)));
        Assert.That(o.SoundOption.CategoryVolume(2), Is.EqualTo(6 * 8 / 100f));
        Assert.That(o.InterfaceOption.UIScale, Is.EqualTo(1f));
        Assert.That(o.BattleOption.TakeDamage, Is.EqualTo(3));
    }
    /// <summary>GameOption.WriteFile: a write that fails part way (here a character XML cannot hold) leaves the file as
    /// it was, whole, and no temporary file; a good write replaces it in the client's form (UTF-8 without a byte order
    /// mark, four-space indent).</summary>
    [Test]
    public void AFailedOptionWriteKeepsTheOldFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ms2-optwrite-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "sub", "GameOption.xml");
            var good = Doc("<ms2><graphic width=\"1920\" height=\"1080\"/><option version=\"21\"/></ms2>");
            GameOption.WriteFile(path, good);
            var whole = File.ReadAllBytes(path);
            Assert.That(whole[0], Is.EqualTo((byte)'<'), "no byte order mark");
            Assert.That(System.Text.Encoding.UTF8.GetString(whole), Does.Contain("\n    <graphic width=\"1920\" height=\"1080\" />"));

            var bad = Doc("<ms2><graphic width=\"1280\" height=\"720\"/><option version=\"21\"/></ms2>");
            bad.Root!.Element("option")!.SetAttributeValue("name", "a\u0001b");   // written after the graphic section
            Assert.That(() => GameOption.WriteFile(path, bad), Throws.Exception);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(whole), "the old file, whole");
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(path)!), Is.EqualTo(new[] { path }), "no temporary file is left");

            bad.Root!.Element("option")!.SetAttributeValue("name", null);
            GameOption.WriteFile(path, bad);
            Assert.That(System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path)), Does.Contain("width=\"1280\""));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(path)!), Is.EqualTo(new[] { path }));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
