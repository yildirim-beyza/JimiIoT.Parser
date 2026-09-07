using JimiIoT.Parser.Framing;

namespace JimiIoT.Parser.Tests;

// Testlerde sentetik ama gerçek checksum kurallarıyla frame üretir.
internal static class TestFrameBuilder
{
    internal static string BuildShortFrameHex(
        byte protocolNumber,
        byte[] informationContent,
        ushort serialNumber = 1)
    {
        int length = 1 + informationContent.Length + 2 + 2;
        if (length > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(informationContent));

        var crcBody = new List<byte>
        {
            (byte)length,
            protocolNumber
        };

        crcBody.AddRange(informationContent);
        crcBody.Add((byte)(serialNumber >> 8));
        crcBody.Add((byte)(serialNumber & 0xFF));

        ushort crc = Crc16X25.Compute(crcBody.ToArray());

        var frame = new List<byte> { 0x78, 0x78 };
        frame.AddRange(crcBody);
        frame.Add((byte)(crc >> 8));
        frame.Add((byte)(crc & 0xFF));
        frame.Add(0x0D);
        frame.Add(0x0A);

        return Convert.ToHexString(frame.ToArray());
    }

    internal static string BuildLongFrameHex(
        byte protocolNumber,
        byte[] informationContent,
        ushort serialNumber = 1)
    {
        int length = 1 + informationContent.Length + 2 + 2;
        if (length > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(informationContent));

        var crcBody = new List<byte>
        {
            (byte)(length >> 8),
            (byte)(length & 0xFF),
            protocolNumber
        };

        crcBody.AddRange(informationContent);
        crcBody.Add((byte)(serialNumber >> 8));
        crcBody.Add((byte)(serialNumber & 0xFF));

        ushort crc = Crc16X25.Compute(crcBody.ToArray());

        var frame = new List<byte> { 0x79, 0x79 };
        frame.AddRange(crcBody);
        frame.Add((byte)(crc >> 8));
        frame.Add((byte)(crc & 0xFF));
        frame.Add(0x0D);
        frame.Add(0x0A);

        return Convert.ToHexString(frame.ToArray());
    }

    internal static string BuildJt808FrameHex(
        ushort messageId,
        byte[] body,
        string terminalIdHex = "0B3A73CE2FF2",
        ushort serialNumber = 1,
        int encryptionType = 0,
        bool subpackaged = false,
        ushort totalPackets = 1,
        ushort packetNumber = 1)
    {
        byte[] terminalId = Convert.FromHexString(terminalIdHex);
        if (terminalId.Length != 6)
            throw new ArgumentException("2013 JT/T 808 test terminal ID 6 byte olmalıdır.", nameof(terminalIdHex));

        return BuildJt808FrameCore(
            messageId,
            body,
            terminalId,
            serialNumber,
            null,
            encryptionType,
            subpackaged,
            totalPackets,
            packetNumber);
    }

    internal static string BuildJt808VersionedFrameHex(
        ushort messageId,
        byte[] body,
        string terminalIdHex = "00000012345678901234",
        byte protocolVersion = 1,
        ushort serialNumber = 1,
        int encryptionType = 0,
        bool subpackaged = false,
        ushort totalPackets = 1,
        ushort packetNumber = 1)
    {
        byte[] terminalId = Convert.FromHexString(terminalIdHex);
        if (terminalId.Length != 10)
            throw new ArgumentException("2019 JT/T 808 test terminal ID 10 byte olmalıdır.", nameof(terminalIdHex));

        return BuildJt808FrameCore(
            messageId,
            body,
            terminalId,
            serialNumber,
            protocolVersion,
            encryptionType,
            subpackaged,
            totalPackets,
            packetNumber);
    }

    private static string BuildJt808FrameCore(
        ushort messageId,
        byte[] body,
        byte[] terminalId,
        ushort serialNumber,
        byte? protocolVersion,
        int encryptionType,
        bool subpackaged,
        ushort totalPackets,
        ushort packetNumber)
    {
        if (body.Length > 0x03FF)
            throw new ArgumentOutOfRangeException(nameof(body));
        if (encryptionType is < 0 or > 7)
            throw new ArgumentOutOfRangeException(nameof(encryptionType));

        ushort properties = (ushort)body.Length;
        properties |= (ushort)(encryptionType << 10);
        if (subpackaged)
            properties |= 0x2000;
        if (protocolVersion.HasValue)
            properties |= 0x4000;

        var payload = new List<byte>
        {
            (byte)(messageId >> 8),
            (byte)(messageId & 0xFF),
            (byte)(properties >> 8),
            (byte)(properties & 0xFF)
        };

        if (protocolVersion.HasValue)
            payload.Add(protocolVersion.Value);

        payload.AddRange(terminalId);
        payload.Add((byte)(serialNumber >> 8));
        payload.Add((byte)(serialNumber & 0xFF));

        if (subpackaged)
        {
            payload.Add((byte)(totalPackets >> 8));
            payload.Add((byte)(totalPackets & 0xFF));
            payload.Add((byte)(packetNumber >> 8));
            payload.Add((byte)(packetNumber & 0xFF));
        }

        payload.AddRange(body);
        payload.Add(Jt808FrameDecoder.CalculateXor(payload.ToArray()));

        return EscapeAndWrapJt808(payload);
    }

    private static string EscapeAndWrapJt808(IEnumerable<byte> payload)
    {
        var escaped = new List<byte> { 0x7E };
        foreach (byte value in payload)
        {
            switch (value)
            {
                case 0x7E:
                    escaped.Add(0x7D);
                    escaped.Add(0x02);
                    break;
                case 0x7D:
                    escaped.Add(0x7D);
                    escaped.Add(0x01);
                    break;
                default:
                    escaped.Add(value);
                    break;
            }
        }

        escaped.Add(0x7E);
        return Convert.ToHexString(escaped.ToArray());
    }
}
