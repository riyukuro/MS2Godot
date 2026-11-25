using System;
using System.Collections.Generic;
using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

internal sealed class RecordedReconnectHost : IReconnectHost
{
    public uint Now { get; set; } = 1000;
    public bool InGame { get; set; } = true;
    public bool HasConnection { get; set; } = true;
    public bool HasNetworkService { get; set; } = true;
    public bool SocketDisconnected { get; set; }
    public bool Closing { get; set; }
    public string SocketHost { get; set; } = "127.0.0.1";
    public int SocketPort { get; set; } = 21001;
    public int SocketSequence { get; set; } = 7;
    public bool IsConnected { get; set; } = true;

    public int ProgressShown;
    public int ProgressHidden;
    public int ConnectCalls;
    public readonly List<int> AttemptsPosted = new();
    public readonly List<int> GaveUpPosted = new();
    public readonly List<string> Notices = new();

    public bool Connect(string host, int port) { ConnectCalls++; return true; }
    public void Close() { IsConnected = false; }
    public void SendReconnectRequest(ReconnectRequest request) { }
    public void ShowProgress(int attempt, int attempts) { ProgressShown++; }
    public void HideProgress() { ProgressHidden++; }
    public void Notice(string text) { Notices.Add(text); }
    public void PostAttempt(int code) { AttemptsPosted.Add(code); }
    public void PostGaveUp(int code) { GaveUpPosted.Add(code); }
    public void EndSession() { }
}

/// <summary>
/// Recorded-packet decode and handler-effect tests for Reconnect (KMS 0x05, GMS2 0x05;
/// CReconnectionManagerCli::OnReconnectPacket @0x140aada20, reconnectionmanagercli.cpp -> ReconnectionManager.OnPacket).
/// </summary>
public class ReconnectRecordedTests
{
    // kms2: 0x05 kind 0, 47 bytes (mode 1, 36-byte session token masked with '*', endpoint id 477813)
    static readonly byte[] KmsContextMode1 = Convert.FromHexString(
        "00010000002400" +
        "2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A" +
        "754A0700");

    // gms2: 0x05 kind 0, 47 bytes (mode 1, 36-byte session token masked with '*', endpoint id 6496)
    static readonly byte[] Gms2ContextMode1 = Convert.FromHexString(
        "00010000002400" +
        "2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A" +
        "60190000");

    // gms2: 0x05 kind 0, 47 bytes (mode 2, 36-byte session token masked with '*', endpoint id 6496)
    static readonly byte[] Gms2ContextMode2 = Convert.FromHexString(
        "00020000002400" +
        "2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A2A" +
        "60190000");

    // gms2: 0x05 kind 2, 5 bytes (result 1 = reconnect succeeded)
    static readonly byte[] Gms2ResultOk = Convert.FromHexString("0201000000");

    static ReconnectionManager EnabledManager(RecordedReconnectHost host)
    {
        var mgr = new ReconnectionManager(host);
        var opts = new ReconnectionOptions();
        opts[ReconnectionOptions.Enabled] = 1;
        opts[ReconnectionOptions.GiveUpSeconds] = 45;
        opts[ReconnectionOptions.MaxAttempts] = 15;
        opts[ReconnectionOptions.RetryDelaySeconds] = 2;
        mgr.SetOptions(opts);
        return mgr;
    }

    [Test]
    public void KmsContextMode1EnablesShouldReconnect()
    {
        var notice = ReconnectNotice.Read(new ByteReader(KmsContextMode1));
        Assert.That(notice.Kind, Is.EqualTo(ReconnectNotice.KindContext));
        Assert.That(notice.Context.Mode, Is.EqualTo(1));
        Assert.That(notice.Context.Host.Length, Is.EqualTo(36));
        Assert.That(notice.Context.Port, Is.EqualTo(477_813));

        var host = new RecordedReconnectHost();
        var mgr = EnabledManager(host);
        Assert.That(mgr.ShouldReconnect(ReconnectionManager.ErrorSocketClosed), Is.False);

        mgr.OnPacket(notice);
        Assert.That(mgr.Context, Is.EqualTo(notice.Context));
        Assert.That(mgr.ShouldReconnect(ReconnectionManager.ErrorSocketClosed), Is.True);
    }

    [Test]
    public void Gms2ContextAndResultDriveReconnectLifecycle()
    {
        var ctx1 = ReconnectNotice.Read(new ByteReader(Gms2ContextMode1));
        Assert.That(ctx1.Kind, Is.EqualTo(ReconnectNotice.KindContext));
        Assert.That(ctx1.Context.Mode, Is.EqualTo(1));
        Assert.That(ctx1.Context.Port, Is.EqualTo(6496));

        var host = new RecordedReconnectHost();
        var mgr = EnabledManager(host);

        mgr.OnPacket(ctx1);
        Assert.That(mgr.Context, Is.EqualTo(ctx1.Context));
        Assert.That(mgr.ShouldReconnect(ReconnectionManager.ErrorSocketClosed), Is.True);

        mgr.Begin(ReconnectionManager.ErrorSocketClosed);
        Assert.That(mgr.IsReconnecting, Is.True);
        Assert.That(mgr.TryConnect(), Is.True);
        Assert.That(mgr.Attempt, Is.EqualTo(1));
        Assert.That(host.ConnectCalls, Is.EqualTo(1));

        var ok = ReconnectNotice.Read(new ByteReader(Gms2ResultOk));
        Assert.That(ok.Kind, Is.EqualTo(ReconnectNotice.KindResult));
        Assert.That(ok.Result, Is.EqualTo(1));

        mgr.OnPacket(ok);
        Assert.That(mgr.IsReconnecting, Is.False);
        Assert.That(mgr.Attempt, Is.EqualTo(0));
        Assert.That(host.ProgressHidden, Is.EqualTo(1));

        // Mode 2 disables reconnect attempts for this context.
        var ctx2 = ReconnectNotice.Read(new ByteReader(Gms2ContextMode2));
        Assert.That(ctx2.Context.Mode, Is.EqualTo(2));
        mgr.OnPacket(ctx2);
        Assert.That(mgr.Context, Is.EqualTo(ctx2.Context));
        Assert.That(mgr.ShouldReconnect(ReconnectionManager.ErrorSocketClosed), Is.False);
    }
}
