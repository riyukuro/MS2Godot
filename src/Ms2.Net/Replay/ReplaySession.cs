using Ms2.Net.Wire;

namespace Ms2.Net.Replay;

/// <summary>A recording played through the real receive path (ReplayStream, MapleChannel, ClientSession) into a FieldTracker.</summary>
public sealed class ReplaySession {
    public ReplayStream Stream { get; }
    public ClientSession Session { get; }
    public FieldTracker Tracker { get; } = new();
    public Task Completion { get; private set; }
    public Exception Error { get; private set; }
    public string Path { get; }

    public ReplaySession(MsbFile file, double speed = 1, double startOffsetSeconds = 0) {
        Path = file.Path;
        Stream = new ReplayStream(file, speed, startOffsetSeconds);
        Session = new ClientSession(new MapleChannel(Stream));
        Stream.ClientPacket += p => { Tracker.Time = Stream.PlayedSeconds; Tracker.Outbound(p); };
        Session.PacketReceived += p => { Tracker.Time = Stream.PlayedSeconds; Tracker.Inbound(p); };
    }

    /// <summary>Starts the read loop on a worker thread. It ends with the recording (the stream end is not an error).</summary>
    public Task Start(CancellationToken ct = default) =>
        Completion = Task.Run(async () => {
            try { await Session.RunAsync(ct); }
            catch (EndOfStreamException) { }
            catch (OperationCanceledException) { }
            catch (Exception e) { Error = e; }
        });
}
