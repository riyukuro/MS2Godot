// Opcode tables for the minimum set. Names are from the client's point of view:
// Inbound = server to client, Outbound = client to server. The restored client's own SendOp/RecvOp enums
// (src/ms2) and the server's enums use the opposite flip of one another; these two do not.
// Values follow the server fork's tables (AGPL-3.0, Maple2) and were checked against recorded KMS traffic.
namespace Ms2.Net;

public enum Inbound : ushort {
    RequestVersion = 0x01,
    RequestKey = 0x04,
    RequestLogin = 0x09,
    LoginResult = 0x0A,
    ServerList = 0x0B,
    CharacterList = 0x0C,
    MoveResult = 0x0D,
    LoginToGame = 0x0E,
    RequestHeartbeat = 0x12,
    RequestClientTickSync = 0x13,
    SyncNumber = 0x14,
    ServerEnter = 0x15,
    RequestFieldEnter = 0x16,
    FieldAddUser = 0x17,
    FieldRemoveUser = 0x18,
    UserSync = 0x1C,
    ProxyGameObj = 0x7E,
    World = 0xB9,
}

public enum Outbound : ushort {
    ResponseVersion = 0x01,
    ResponseLogin = 0x03,
    ResponseKey = 0x04,
    CharacterManagement = 0x09,
    ResponseHeartbeat = 0x0A,
    RequestTimeSync = 0x0B,
    ResponseClientTickSync = 0x0C,
    ResponseFieldEnter = 0x10,
    UserSync = 0x12,
    RequestServerList = 0x14,
    State = 0x80,
}
