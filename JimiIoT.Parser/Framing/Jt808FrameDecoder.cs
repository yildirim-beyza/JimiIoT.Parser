namespace JimiIoT.Parser.Framing;

// ============================================================================
// Jt808FrameDecoder
// ----------------------------------------------------------------------------
// JT/T 808 frame işlerini tek yerde tutar:
//   1) 0x7E başlangıç/bitiş sınırı
//   2) 0x7D01 -> 0x7D ve 0x7D02 -> 0x7E unescape
//   3) 1 byte XOR checksum doğrulaması
//   4) message header çözümü
//   5) body length sınırı
//
// VL502/VG502 V1.1.1 dokümanı JT/T 808-2013 tabanlıdır; decoder ayrıca 2019'daki
// version flag'li header biçimini de kabul edecek şekilde yazılmıştır.
// ============================================================================
internal sealed class Jt808FrameDecoder
{
    internal bool TryDecode(ReadOnlySpan<byte> buffer, out Jt808Frame frame, out int consumed)
    {
        frame = null!;
        consumed = 0;

        // En küçük JT/T 808 frame: 7E + 12 byte 2013 header + checksum + 7E.
        if (buffer.Length < 15 || buffer[0] != 0x7E || buffer[^1] != 0x7E)
            return false;

        if (!TryUnescape(buffer[1..^1], out byte[] unescaped))
            return false;

        // Unescaped bölüm: header + body + checksum.
        if (unescaped.Length < 13)
            return false;

        byte expectedChecksum = unescaped[^1];
        byte calculatedChecksum = CalculateXor(unescaped.AsSpan(0, unescaped.Length - 1));
        if (expectedChecksum != calculatedChecksum)
            return false;

        ReadOnlySpan<byte> payload = unescaped.AsSpan(0, unescaped.Length - 1);

        ushort messageId = ReadUInt16BigEndian(payload, 0);
        ushort properties = ReadUInt16BigEndian(payload, 2);
        int bodyLength = properties & 0x03FF;
        bool subpackaged = (properties & 0x2000) != 0;
        bool versioned = (properties & 0x4000) != 0;

        int offset = 4;
        byte? protocolVersion = null;
        int terminalLength;

        if (versioned)
        {
            // JT/T 808-2019: protocol version 1B + terminal id 10B.
            if (payload.Length < offset + 1 + 10 + 2)
                return false;

            protocolVersion = payload[offset++];
            terminalLength = 10;
        }
        else
        {
            // JT/T 808-2011/2013 ve VL502/VG502 V1.1.1: terminal id 6B.
            terminalLength = 6;
        }

        if (payload.Length < offset + terminalLength + 2)
            return false;

        byte[] terminalId = payload.Slice(offset, terminalLength).ToArray();
        offset += terminalLength;

        ushort serialNumber = ReadUInt16BigEndian(payload, offset);
        offset += 2;

        ushort? totalPackets = null;
        ushort? packetNumber = null;

        if (subpackaged)
        {
            if (payload.Length < offset + 4)
                return false;

            totalPackets = ReadUInt16BigEndian(payload, offset);
            packetNumber = ReadUInt16BigEndian(payload, offset + 2);
            offset += 4;
        }

        // Şifreli body framing seviyesinde geçerli olabilir; bu decoder yalnız zarfı
        // çözer. Body semantiği protocol parser'ın sorumluluğudur.
        if (payload.Length - offset != bodyLength)
            return false;

        frame = new Jt808Frame
        {
            MessageId = messageId,
            BodyProperties = properties,
            TerminalId = terminalId,
            SerialNumber = serialNumber,
            Body = payload.Slice(offset, bodyLength).ToArray(),
            ProtocolVersion = protocolVersion,
            TotalPackets = totalPackets,
            PacketNumber = packetNumber
        };

        consumed = buffer.Length;
        return true;
    }

    internal static byte CalculateXor(ReadOnlySpan<byte> data)
    {
        byte checksum = 0;
        foreach (byte value in data)
            checksum ^= value;

        return checksum;
    }

    private static bool TryUnescape(ReadOnlySpan<byte> escaped, out byte[] unescaped)
    {
        var result = new List<byte>(escaped.Length);

        for (int i = 0; i < escaped.Length; i++)
        {
            byte value = escaped[i];

            if (value != 0x7D)
            {
                result.Add(value);
                continue;
            }

            if (i + 1 >= escaped.Length)
            {
                unescaped = Array.Empty<byte>();
                return false;
            }

            byte next = escaped[++i];
            switch (next)
            {
                case 0x01:
                    result.Add(0x7D);
                    break;
                case 0x02:
                    result.Add(0x7E);
                    break;
                default:
                    unescaped = Array.Empty<byte>();
                    return false;
            }
        }

        unescaped = result.ToArray();
        return true;
    }

    private static ushort ReadUInt16BigEndian(ReadOnlySpan<byte> source, int offset)
        => (ushort)((source[offset] << 8) | source[offset + 1]);
}
