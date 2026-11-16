# Ms2.Net

Client network library (net8.0, no Godot dependency). Offline by design: `MapleChannel` and `ClientSession` work on any `Stream`; this library never opens a socket itself.

- `Wire/`: the frame and cipher (Maple2.PacketLib 1.0.2, GPL-3.0). Frame = u16 seqbase + i32 len + encrypted payload (opcode inside). `Handshake` reads the server's plain first frame; the client sends with the server's `riv` and receives with the server's `siv` stepped once (CRT rand) for the hello.
- `Opcodes.cs`: `Inbound` (server to client) and `Outbound` (client to server). These names are from the client's point of view; the restored client's `SendOp`/`RecvOp` and the server's enums flip each other.
- `Packets/`: typed readers and writers for the minimum path: login, character list, select, enter field, own movement, others' add/remove/move.
- `ClientSession`: read loop, answers heartbeat (in 0x12) and client tick sync (in 0x13) itself on the net thread, forwards everything else, counts unknown opcodes.

Layouts come from the server fork's writers (Maple2, AGPL-3.0) and were checked against the restored client where noted in the file comments and against recorded KMS2 traffic (see tests). Build 2525 is the target; recordings from builds 2520 and up are decoded in the tests, older builds differ in the user sync record and FieldAddUser.

## Tests

`dotnet test tests/Ms2.Net.Tests` (the box needs .NET 8, or `DOTNET_ROLL_FORWARD=Major` with a newer runtime; the test project sets `RollForward`). Cipher, framing, session and layout round trips always run. Decoding of recorded traffic needs `MS2_SNIFFS` set to a folder of MapleShark2 `.msb` files; it is skipped otherwise. Recordings are never copied into the repo.
