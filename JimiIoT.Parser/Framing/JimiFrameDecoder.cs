namespace JimiIoT.Parser.Framing;

// ============================================================================
// JimiFrameDecoder
// ----------------------------------------------------------------------------
// Ham JIMI Protocol paketini açar. Bu sınıf payload'un "ne anlama geldiğini"
// çözmez; yalnızca frame sınırını, length alanını, protokol numarasını,
// serial number'ı, CRC'yi ve 0x0D0A bitiş işaretini doğrular.
//
// Paket:
// [78 78 | 79 79] [Length 1-2B] [Protocol 1B] [Info NB]
// [Serial 2B] [CRC 2B] [0D 0A]
//
// Rehber karşılığı: JIMI/GT06 frame yapısı ve complete-frame doğrulaması.
// ============================================================================
internal sealed class JimiFrameDecoder
{
    internal bool TryDecode(
        ReadOnlySpan<byte> buffer,
        out DeviceFrame frame,
        out int consumed)
    {
        frame = null!;
        consumed = 0;

        // Bir frame'in en başını bile okuyamayacağımız kadar kısa veri geldiyse
        // TCP'den daha fazla byte beklenmelidir; exception değil false döneriz.
        if (buffer.Length < 5)
            return false;

        bool isShortFrame = buffer[0] == 0x78 && buffer[1] == 0x78;
        bool isLongFrame = buffer[0] == 0x79 && buffer[1] == 0x79;

        if (!isShortFrame && !isLongFrame)
            return false;

        int offset = 2;
        int crcStart = offset; // CRC, length alanından başlar.

        int lengthFieldSize = isShortFrame ? 1 : 2;
        if (offset + lengthFieldSize > buffer.Length)
            return false;

        int length = isShortFrame
            ? buffer[offset]
            : (buffer[offset] << 8) | buffer[offset + 1];

        offset += lengthFieldSize;

        // Length; Protocol(1) + Info(N) + Serial(2) + CRC(2) toplamıdır.
        // Dolayısıyla en küçük anlamlı length 5'tir.
        if (length < 5)
            return false;

        if (offset >= buffer.Length)
            return false;

        byte protocolNumber = buffer[offset++];

        int informationLength = length - 1 - 2 - 2;
        if (informationLength < 0 || offset + informationLength > buffer.Length)
            return false;

        byte[] informationContent = buffer.Slice(offset, informationLength).ToArray();
        offset += informationLength;

        if (offset + 2 > buffer.Length)
            return false;

        ushort serialNumber = (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
        offset += 2;

        int crcEnd = offset;

        if (offset + 2 > buffer.Length)
            return false;

        ushort packetChecksum = (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
        offset += 2;

        ushort calculatedChecksum =
            Crc16X25.Compute(buffer.Slice(crcStart, crcEnd - crcStart));

        if (packetChecksum != calculatedChecksum)
            return false;

        if (offset + 2 > buffer.Length)
            return false;

        if (buffer[offset] != 0x0D || buffer[offset + 1] != 0x0A)
            return false;

        offset += 2;

        frame = new DeviceFrame
        {
            LengthFieldSize = lengthFieldSize,
            Length = length,
            ProtocolNumber = protocolNumber,
            InformationContent = informationContent,
            SerialNumber = serialNumber,
            Checksum = packetChecksum
        };

        // TCP buffer'ında arka arkaya birden fazla frame bulunabilir.
        // consumed, çağırana bu frame için kaç byte tüketildiğini bildirir.
        consumed = offset;
        return true;
    }
}
