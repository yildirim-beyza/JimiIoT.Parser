namespace JimiIoT.Parser.Protocols;

// "Magic number"ları ana parser'a yaymamak için mesaj numaralarını tek yerde tutar.
internal static class JimiProtocolNumbers
{
    internal const byte Login = 0x01;
    internal const byte Status = 0x13;
    internal const byte StringMessage = 0x15;
    internal const byte Heartbeat = 0x23;
    internal const byte Status2 = 0x36;
    internal const byte Dtc = 0x65;
    internal const byte Pid = 0x66;
    internal const byte Obd = 0x8C;
    internal const byte Info = 0x94;

    internal static bool IsGps(byte protocolNumber)
        => protocolNumber is 0x10 or 0x12 or 0x16 or 0x22 or 0x26 or 0x27 or 0xA0;
}
