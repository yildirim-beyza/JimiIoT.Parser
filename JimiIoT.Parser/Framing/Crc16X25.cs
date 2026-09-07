namespace JimiIoT.Parser.Framing;

// ============================================================================
// Crc16X25
// ----------------------------------------------------------------------------
// JIMI/GT06 paketlerinde kullanılan CRC-16/X-25 kontrol değerini hesaplar.
// Bu bir şifreleme veya kimlik doğrulama değildir; veri bütünlüğü kontrolüdür.
//
// Parametreler:
//   Poly   : 0x1021
//   Init   : 0xFFFF
//   RefIn  : true
//   RefOut : true
//   XorOut : 0xFFFF
//
// Kaynak yaklaşımı: Traccar GT06 checksum implementasyonu + standart
// "123456789" -> 0x906E test vektörüyle çapraz kontrol.
// Rehber karşılığı: JIMI/GT06 CRC bütünlük kontrolü.
// ============================================================================
internal static class Crc16X25
{
    private const int Polynomial = 0x1021;
    private const int InitialValue = 0xFFFF;
    private const int FinalXor = 0xFFFF;

    private static readonly int[] LookupTable = BuildLookupTable();

    private static int[] BuildLookupTable()
    {
        var table = new int[256];

        for (int i = 0; i < table.Length; i++)
        {
            int crc = i << 8;

            for (int bit = 0; bit < 8; bit++)
            {
                bool highBitSet = (crc & 0x8000) != 0;
                crc <<= 1;

                if (highBitSet)
                    crc ^= Polynomial;
            }

            table[i] = crc & 0xFFFF;
        }

        return table;
    }

    private static int ReverseBits(int value, int bitCount)
    {
        int result = 0;

        for (int i = 0; i < bitCount; i++)
        {
            result = (result << 1) | (value & 1);
            value >>= 1;
        }

        return result;
    }

    internal static ushort Compute(ReadOnlySpan<byte> data)
    {
        int crc = InitialValue;

        foreach (byte rawByte in data)
        {
            int reflectedByte = ReverseBits(rawByte, 8);
            int tableIndex = ((crc >> 8) & 0xFF) ^ reflectedByte;
            crc = (crc << 8) ^ LookupTable[tableIndex];
        }

        crc = ReverseBits(crc, 16);
        return (ushort)((crc ^ FinalXor) & 0xFFFF);
    }
}
