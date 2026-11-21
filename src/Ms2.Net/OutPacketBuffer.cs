using Ms2.Net.Packets;
using Ms2.Net.Wire;

namespace Ms2.Net;

/// <summary>
/// COutPacketBuffer (clientreconnectionstructs.cpp:130-300): numbers every packet the client sends, keeps a copy of the ones a server
/// could have missed, and sends them again when the server asks after a reconnect. No sockets: the caller passes the send function.
/// STILL-OPEN: the loopback packets (m_loopback, packets the client queues for itself) are not modelled; the size of a captured packet
/// counts its body only (the client counts COutPacket::GetLength).
/// </summary>
public sealed class OutPacketBuffer {
    /// <summary>The client's captured length is COutPacket::GetLength of the clone: the u16 type is part of the buffer (packet.cpp:227-269, clientreconnectionstructs.cpp:235-237), so body + 2.</summary>
    static int SizeOf(Captured c) => c.Body.Length + 2;

    sealed record Captured(ushort Type, byte[] Body) { public int Sequence; }

    readonly List<Captured> captured = new(), pending = new();
    readonly object gate = new();
    int sequence, capturedSize, limit;
    bool captureEnabled, resending;

    /// <summary>COutPacketBuffer::GetSequence: the number of the last packet sent.</summary>
    public int Sequence { get { lock (gate) return sequence; } }
    public int CapturedCount { get { lock (gate) return captured.Count; } }
    public int PendingCount { get { lock (gate) return pending.Count; } }
    public bool CaptureEnabled { get { lock (gate) return captureEnabled; } }

    /// <summary>COutPacketBuffer::Reset (@0x140208d40): everything back to zero, the lists empty. The client does it at ResponseKey (CGameSocket @0x1412672d0).</summary>
    public void Reset() {
        lock (gate) { resending = false; captureEnabled = false; limit = 0; capturedSize = 0; sequence = 0; captured.Clear(); pending.Clear(); }
    }

    /// <summary>CGameSocket::FUN_1412672d0 after the reset: the options' enabled flag turns capture on, its limit is the size that triggers a shrink.</summary>
    public void Configure(ReconnectionOptions options) {
        lock (gate) {
            if (options[ReconnectionOptions.Enabled] == 1) limit = options[ReconnectionOptions.BufferLimit];
            captureEnabled = options[ReconnectionOptions.Enabled] == 1;
            if (!captureEnabled) Shrink();
        }
    }

    /// <summary>SetCaptureEnabled (@0x140209450): turning it off drops the copies.</summary>
    public void SetCaptureEnabled(bool enabled) { lock (gate) { captureEnabled = enabled; if (!enabled) Shrink(); } }
    public void SetLimit(int value) { lock (gate) limit = value; }

    /// <summary>IsControlPacket (@0x140209640) with bControl false, as CapturePacket's callers pass it: the handshake and keepalive types 1 to 8 are never kept.</summary>
    public static bool IsControl(ushort type, bool control = false) => control ? type is >= 1 and <= 12 : type is >= 1 and <= 8;

    /// <summary>CapturePacket (@0x140209510): while not resending, the packet gets the next number; a copy is kept when capture is on and it is no control packet. Returns the number.</summary>
    public int Capture(ushort type, byte[] body) {
        lock (gate) return CaptureLocked(type, body);
    }

    int CaptureLocked(ushort type, byte[] body) {
        if (resending) return sequence;
        sequence++;
        if (captureEnabled && !IsControl(type)) { captured.Add(new Captured(type, (byte[]) body.Clone()) { Sequence = sequence }); capturedSize += body.Length + 2; }
        return sequence;
    }

    /// <summary>PushPendingPacket (@0x1402091b0): holds a packet while the socket is down; <see cref="Submit"/> sends it ahead of the next one.</summary>
    public void PushPending(ushort type, byte[] body) { lock (gate) pending.Add(new Captured(type, (byte[]) body.Clone())); }

    /// <summary>FUN_140208960 (@0x140208960): sends the pending packets first (each numbered and captured), then this one.</summary>
    public void Submit(ushort type, byte[] body, Action<ushort, byte[]> send) {
        lock (gate) {
            foreach (var p in pending) { CaptureLocked(p.Type, p.Body); send(p.Type, p.Body); }
            pending.Clear();
            CaptureLocked(type, body);
            send(type, body);
        }
    }

    /// <summary>
    /// DiscardAcked (@0x140209050), the server's Ping carries the number it has seen: drops copies from the head while both the head and the one after it are at or below
    /// it, so the newest acknowledged copy stays.
    /// </summary>
    public void DiscardAcked(int ack) {
        lock (gate) {
            while (captured.Count > 0) {
                if (captured[0].Sequence > ack) break;
                if (captured.Count == 1) break;
                if (captured[1].Sequence > ack) break;
                capturedSize -= SizeOf(captured[0]);
                captured.RemoveAt(0);
            }
        }
    }

    /// <summary>ShrinkCapturedPackets (@0x140208e30): capture off drops all; on, once the kept size reaches the limit it drops from the head down to half of it.</summary>
    public void Shrink() {
        lock (gate) {
            if (!captureEnabled) { if (capturedSize != 0) { captured.Clear(); capturedSize = 0; } return; }
            int shrink = limit / 2;
            if (capturedSize < limit) return;
            while (capturedSize > shrink && captured.Count > 0) { capturedSize -= SizeOf(captured[0]); captured.RemoveAt(0); }
        }
    }

    /// <summary>
    /// RetransmitClientPackets (@0x140208ab0): sends ReconnectUnk with the server's number, every kept packet after it renumbered from there on, then FinalizeReconnect
    /// with the current sequence. While it runs nothing is numbered again.
    /// </summary>
    public void Retransmit(int lastSequence, Action<ushort, byte[]> send) {
        lock (gate) {
            resending = true;
            try {
                send((ushort) Outbound.ReconnectUnk, Write(new Packets.ReconnectAck(lastSequence)));
                int next = lastSequence;
                foreach (var c in captured) {
                    if (c.Sequence <= lastSequence) continue;
                    c.Sequence = ++next;
                    send(c.Type, c.Body);
                }
                send((ushort) Outbound.FinalizeReconnect, Write(new Packets.FinalizeReconnect(sequence)));
            } finally { resending = false; }
        }
    }

    static byte[] Write(IOutPacket p) { var w = new Maple2.PacketLib.Tools.ByteWriter(); p.Write(w); return w.Buffer[..w.Length]; }
}
