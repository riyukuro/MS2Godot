using System.Text.Json.Nodes;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// CUIService::IsTextFieldFocused (@0x141adbb00, uiservice.cpp:1364) -> GFxManager::IsTextFieldFocused(2) (@0x1422af2d0,
/// gfxmanager.cpp:531) -> IMEManager::IsTextFieldFocused (@0x142544ef0, sfr_as3_20.cpp:347): one movie, main.gfx, holds every
/// dialog, so a focused input field of ANY dialog (the UIMessageBox layer's name prompts included, a UIHeadUpDisplay,
/// uimessagebox.cpp:371) turns the input hold on (InputBlock.Active, FUN_14116d7a0 mask 2: skill, quick slot, window and
/// move keys). Here a movie outside the scene's UiStage registers with <see cref="OffStageTextFocus"/>; GitHub issue 13: the
/// guild name prompt let a, s, d, f cast and i open the inventory.
/// </summary>
[NonParallelizable]   // sets process-wide hooks the product reads
public class TextFocusKeysTests
{
    Func<bool> saved;

    [SetUp] public void SetUp() => saved = InputBlock.TextFieldFocused;
    [TearDown] public void TearDown() => InputBlock.TextFieldFocused = saved;

    static Player Movie()
    {
        var rt = new Runtime();
        var layout = new JsonObject { ["movie"] = "t", ["stage"] = new JsonObject { ["width"] = 800, ["height"] = 600 }, ["symbols"] = new JsonObject(), ["root"] = new JsonObject() };
        return new Player(rt, layout);
    }

    static AsTextField Field(Player p, string type = "input")
    {
        var tf = (AsTextField)p.RT.GetClass("flash.text.TextField").Construct();
        tf.type = type;
        p.Stage.addChild(tf);
        return tf;
    }

    /// <summary>The game's gate (HudScene): the scene's stage, the movies outside it, nothing else here.</summary>
    static void Gate(UiStage stage) => InputBlock.TextFieldFocused = () => stage.TextFieldFocused || OffStageTextFocus.Focused;

    [Test]
    public void AFocusedInputFieldOfAMovieOutsideTheStageHoldsTheInput()
    {
        var stage = new UiStage();
        Gate(stage);
        var box = Movie();
        var tf = Field(box);
        bool shown = true;
        OffStageTextFocus.Add(box, () => shown);
        try
        {
            Assert.That(InputBlock.Active, Is.False, "nothing focused");
            box.Stage.focus = tf;
            Assert.That((InputBlock.Active, InputBlock.OwnKeysBlocked, InputBlock.TextFieldFocused()), Is.EqualTo((true, true, true)), "an input field");
            tf.displayAsPassword = true;
            Assert.That(InputBlock.Active, Is.False, "a password field takes no IME");
            tf.displayAsPassword = false;
            box.RT.Natives["scaleform.gfx.TextFieldEx/setIMEEnabled"].Invoke(null, new object[] { tf, false });
            Assert.That(InputBlock.Active, Is.False, "TextFieldEx.setIMEEnabled(false)");
            box.RT.Natives["scaleform.gfx.TextFieldEx/setIMEEnabled"].Invoke(null, new object[] { tf, true });
            Assert.That(InputBlock.Active, Is.True);
            shown = false;
            Assert.That(InputBlock.Active, Is.False, "a hidden movie");
            shown = true;
            box.Stage.focus = null;
            Assert.That(InputBlock.Active, Is.False, "focus left the field: the keys act again");
            box.Stage.focus = tf;
            Assert.That(InputBlock.Active, Is.True);
        }
        finally { OffStageTextFocus.Remove(box); }
        Assert.That(InputBlock.Active, Is.False, "the movie left the tree");
    }

    [Test]
    public void ADynamicFieldAndAFieldOffTheStageHoldNothing()
    {
        var stage = new UiStage();
        Gate(stage);
        var box = Movie();
        var tf = Field(box, "dynamic");
        OffStageTextFocus.Add(box, () => true);
        try
        {
            box.Stage.focus = tf;
            Assert.That(InputBlock.Active, Is.False, "a dynamic field");
            tf.type = "input";
            Assert.That(InputBlock.Active, Is.True);
            box.Stage.removeChild(tf);
            Assert.That(InputBlock.Active, Is.False, "the field left the stage");
        }
        finally { OffStageTextFocus.Remove(box); }
    }

    [Test]
    public void ASceneStageAndAnOffStageMovieEachCount()
    {
        var stage = new UiStage();
        Gate(stage);
        var on = Movie();
        var off = Movie();
        stage.Add(on, () => (0, 0), () => 0);
        OffStageTextFocus.Add(off, () => true);
        try
        {
            var a = Field(on);
            var b = Field(off);
            on.Stage.focus = a;
            Assert.That(InputBlock.Active, Is.True, "the stage's field");
            on.Stage.focus = null;
            off.Stage.focus = b;
            Assert.That(InputBlock.Active, Is.True, "the off-stage field");
            off.Stage.focus = null;
            Assert.That(InputBlock.Active, Is.False);
        }
        finally { OffStageTextFocus.Remove(off); stage.Remove(on); }
    }

    /// <summary>
    /// The whole list: every movie of Resource/Gfx with an editable text field. Each one, started as the game starts it, has its
    /// input fields focused one at a time: the gate must see the field (on the scene's stage and off it), and let go when focus leaves.
    /// Password fields and IME-disabled fields are the client's exceptions (TextField::IsIMEEnabled).
    /// </summary>
    [Test]
    public void EveryMovieWithAnEditableFieldHoldsTheInputWhileItHasFocus()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var packs = TestData.Packs(d);
        var stage = new UiStage();
        Gate(stage);
        int movies = 0, fields = 0, exempt = 0, refused = 0, offStageFields = 0, lateFields = 0;
        var bad = new List<string>();
        var names = new List<string>();
        foreach (var key in packs.List("Resource/Gfx").Where(n => n.EndsWith(".gfx", StringComparison.OrdinalIgnoreCase))
                     .Select(n => Path.GetFileNameWithoutExtension(n)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Player p;
            try
            {
                var (_, pl) = TestData.SweepSource(packs, key).Open(key, r => r.Budget = 20_000_000);
                p = pl;
                p.StageW = 1280; p.StageH = 720;
                if (!p.Start()) continue;
                for (int i = 0; i < 10; i++) p.Tick();
            }
            catch (Exception) { continue; }
            var inputs = new List<AsTextField>();
            void Walk(AsDisplayObject o)
            {
                if (o is AsTextField t && t.type == "input" && !inputs.Contains(t)) inputs.Add(t);
                if (o is AsContainer c) foreach (var ch in c.Children.ToArray()) Walk(ch);
            }
            Walk(p.Stage);
            // fields a movie makes after its start (a timer, a load that finishes later): a second read after more ticks
            int atStart = inputs.Count;
            try { for (int i = 0; i < 110; i++) p.Tick(); } catch (Exception) { }
            Walk(p.Stage);
            lateFields += inputs.Count - atStart;
            if (inputs.Count == 0) continue;
            movies++;
            names.Add($"{key}:{inputs.Count}");
            foreach (var tf in inputs)
            {
                if (!tf.OnStage) { offStageFields++; continue; }
                p.Stage.focus = tf;
                for (int i = 0; i < 3 && !ReferenceEquals(p.Focus, tf); i++) p.Tick();
                if (!ReferenceEquals(p.Focus, tf)) { refused++; if (refused < 6) TestContext.Out.WriteLine($"refused {key}/{tf.name}: focus={p.Focus?.GetType().Name} sel={tf.selectable} vis={tf.visible} rtp={ReferenceEquals(p.RT.Player, p)}"); continue; }     // not focus-enabled (tabEnabled/selectable off): the client cannot focus it
                bool takes = !tf.displayAsPassword && !tf.ImeDisabled;
                fields++;
                if (!takes) { exempt++; continue; }
                stage.Add(p, () => (0, 0), () => 0);
                bool onStage = InputBlock.Active;
                stage.Remove(p);
                OffStageTextFocus.Add(p, () => true);
                bool offStage = InputBlock.Active;
                OffStageTextFocus.Remove(p);
                p.Stage.focus = null;
                bool released = !InputBlock.Active;
                if (!onStage || !offStage || !released) bad.Add($"{key}/{tf.name}: stage={onStage} off={offStage} released={released}");
            }
        }
        TestContext.Out.WriteLine($"movies with an input field: {movies}, focused: {fields} (password/IME-off exempt {exempt}), focus refused: {refused}, not on the stage at start: {offStageFields}, made after the first 10 ticks: {lateFields}");
        TestContext.Out.WriteLine(string.Join(" ", names));
        Assert.That(bad, Is.Empty);
        Assert.That(movies, Is.GreaterThan(5), "the sweep found the movies");
    }
}
