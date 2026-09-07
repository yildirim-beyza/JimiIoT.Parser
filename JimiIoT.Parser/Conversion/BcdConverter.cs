namespace JimiIoT.Parser.Conversion;

// ============================================================================
// BcdConverter
// ----------------------------------------------------------------------------
// JIMI 0x01 login paketindeki 8 byte BCD terminal ID alanını 15 haneli
// IMEI'ye çevirir.
//
// Örnek:
// 03 53 41 90 36 06 60 61 -> 0353419036066061 -> 353419036066061
// Baştaki ilk hane, 15 haneli IMEI'yi 8 byte'a sığdırmak için kullanılan
// padding hanesidir ve atılır.
//
// Rehber karşılığı: JIMI login BCD/IMEI kuralı.
// ============================================================================
internal static class BcdConverter
{
    internal static string ToImei15(ReadOnlySpan<byte> bcdBytes)
    {
        if (bcdBytes.Length < 8)
            throw new ArgumentException("IMEI için en az 8 BCD byte gerekir.", nameof(bcdBytes));

        var digits = new char[16];

        for (int i = 0; i < 8; i++)
        {
            int highNibble = (bcdBytes[i] >> 4) & 0x0F;
            int lowNibble = bcdBytes[i] & 0x0F;

            // BCD yalnız 0-9 rakamlarını taşımalıdır. A-F görülürse veri bozuktur.
            if (highNibble > 9 || lowNibble > 9)
                throw new FormatException("IMEI alanında geçersiz BCD nibble bulundu.");

            digits[i * 2] = (char)('0' + highNibble);
            digits[i * 2 + 1] = (char)('0' + lowNibble);
        }

        return new string(digits, 1, 15);
    }
}
