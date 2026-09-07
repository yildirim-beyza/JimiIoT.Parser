namespace JimiIoT.Parser.Protocols;

// Protokol ID'lerini magic number olarak ana parser'a dağıtmamak için tek yerde tutulur.
internal static class Jt808ProtocolNumbers
{
    internal const ushort Heartbeat = 0x0002;
    internal const ushort Register = 0x0100;
    internal const ushort Authenticate = 0x0102;
    internal const ushort ParameterQueryResponse = 0x0104;
    internal const ushort Location = 0x0200;
    internal const ushort TransparentUplink = 0x0900;

    internal const byte VehicleTransparentType = 0xF0;
    internal const byte ObdSubtype = 0x01;
    internal const byte DtcSubtype = 0x02;
    internal const byte VinSubtype = 0x0B;
}
