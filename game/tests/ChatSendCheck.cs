using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using Ms2.Avm2.Flash;
using Ms2Godot.Ui;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/chat_send.tscn): a chat line typed and sent with the keyboard, as a player does, offline
/// and then with a session. Each round opens the HUD (res://ui/hud.tscn), presses Return (UIMessenger::vfunc_13,
/// uimessenger.cpp:810: FUN_141a11e30 gives the unfocused input the focus), types q and z, presses Return again (the
/// focused input's text goes to MessengerBrowser.SendUserChat, CommandChat kind 0) and checks that the input took the
/// focus, held "qz" and was released and emptied. Offline the line must reach the chat log; with a session (a
/// LiveSession without a connection, as the instance, the HUD opened again so the first HUD leaves the tree) the HUD
/// must have wired the send, and Return must send one request with "qz". Quits with 0 when all of that holds, else 1.
/// </summary>
public partial class ChatSendCheck : Node
{
    public override void _Ready()
    {
        GetTree().Root.Size = new Vector2I(1280, 720);   // a headless window is 64x64
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, new ChatSendDriver());
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, "res://ui/hud.tscn");
    }
}

/// <summary>The check's driver, at the tree root so it outlives the scene changes to the HUD.</summary>
public partial class ChatSendDriver : Node
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    HudScene hud, firstHud;
    MovieDialog chat;
    Ms2.Ui.Hud.MessengerController side;
    AsTextField field;
    readonly List<Ms2.Net.Packets.UserChatRequest> sent = new();
    int stage, wait, logsBefore;
    bool live;
    string offlineResult;
    readonly ulong startMs = Time.GetTicksMsec();

    static void Tap(Key key, char ch = '\0')
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = ch, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    void Finish(bool ok, string what)
    {
        if (Net.LiveSession.Instance is { } s && !s.IsInsideTree())
        {
            typeof(Net.LiveSession).GetProperty("Instance")!.SetValue(null, null);
            s.Free();
        }
        GD.Print("chat send: " + (ok ? "OK " : "FAIL ") + (live ? "live: " : "offline: ") + what);
        GetTree().Quit(ok ? 0 : 1);
        SetProcess(false);
    }

    public override void _Process(double delta)
    {
        if (Time.GetTicksMsec() - startMs > 300_000) { Finish(false, $"timed out at stage {stage}"); return; }
        switch (stage)
        {
            case 0:
            {
                if (GetTree().CurrentScene is not HudScene h || h == firstHud || !Field.LoadComplete) return;
                if (++wait < 30) return;
                hud = h;
                var list = (List<(MovieDialog Movie, Ms2.Ui.Hud.MessengerController Side)>)typeof(HudScene).GetField("messengers", Private)!.GetValue(hud);
                if (list.Count == 0 || !list[0].Movie.Loaded) { if (wait > 600) Finish(false, "no chat window"); return; }
                (chat, side) = list[0];
                if (chat.Host.Child("m_textInput") is not { } ti || chat.Host.Get(ti, "textField") is not AsTextField tf) { Finish(false, "no chat m_textInput"); return; }
                field = tf;
                sent.Clear();
                if (live)
                {
                    // the session's send, as the HUD wired it; kept, so the request still goes the way the HUD sends it
                    if (HudScene.ChatLog.SendPacket is not { } wired) { Finish(false, "the HUD opened with a session wired no chat send"); return; }
                    HudScene.ChatLog.SendPacket = r => { sent.Add(r); wired(r); };
                }
                logsBefore = HudScene.ChatLog.Logs.Count;
                if (side.InputActive) { Finish(false, "the chat input is focused before Return"); return; }
                Tap(Key.Enter);
                wait = 0; stage = 1;
                return;
            }
            case 1:
            {
                if (++wait < 10) return;
                if (!side.InputActive) { Finish(false, $"Return gave the chat input no focus (movie focus {chat.Player.Focus})"); return; }
                if (!ReferenceEquals(chat.Player.Focus, field)) { Finish(false, $"Return focused the input but the movie focus is {chat.Player.Focus}"); return; }
                Tap(Key.Q, 'q');
                Tap(Key.Z, 'z');
                wait = 0; stage = 2;
                return;
            }
            case 2:
            {
                if (++wait < 10) return;
                if (field.Text != "qz") { Finish(false, $"typed qz with the input focused: input \"{field.Text}\""); return; }
                Tap(Key.Enter);
                wait = 0; stage = 3;
                return;
            }
            case 3:
            {
                if (++wait < 10) return;
                if (side.InputActive || field.Text != "") { Finish(false, $"Return left the input focused {side.InputActive} with \"{field.Text}\""); return; }
                var added = HudScene.ChatLog.Logs.Skip(logsBefore).Where(l => l.Msg.Contains("qz")).ToList();
                if (!live)
                {
                    if (added.Count != 1) { Finish(false, $"Return with \"qz\" added {added.Count} chat log lines with it"); return; }
                    offlineResult = "the line reached the chat log";
                    // the session round: a LiveSession without a connection as the instance, the HUD opened again
                    var session = new Net.LiveSession();
                    typeof(Net.LiveSession).GetProperty("Instance")!.SetValue(null, session);
                    live = true;
                    firstHud = hud;
                    GetTree().ChangeSceneToFile("res://ui/hud.tscn");
                    wait = 0; stage = 0;
                    return;
                }
                string got = string.Join(", ", sent.ConvertAll(r => $"kind {r.Kind} \"{r.Message}\""));
                if (sent.Count != 1 || sent[0].Message != "qz" || sent[0].Kind != 0) { Finish(false, $"Return with \"qz\" sent [{got}]"); return; }
                Finish(true, $"offline {offlineResult}; with a session Return sent [{got}] and released the input");
                return;
            }
        }
    }
}
