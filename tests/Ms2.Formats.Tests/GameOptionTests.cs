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
}
