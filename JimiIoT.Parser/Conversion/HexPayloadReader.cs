namespace JimiIoT.Parser.Conversion;

// ============================================================================
// HexPayloadReader
// ----------------------------------------------------------------------------
// Public imza string deviceData kabul eder; protokol
// kodu ise byte[] ile çalışır. Bu sınıf sınırdaki dönüşümü yapar:
//
//   "78 78 0D 01 ..."  -> byte[]
//   "78780D01..."      -> byte[]
//
// Rehber karşılığı: public GetDeviceInfo HEX girdi sözleşmesi.
// ============================================================================
internal static class HexPayloadReader
{
    internal static byte[] ToBytes(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            throw new ArgumentException("deviceData boş olamaz.", nameof(hex));

        // Yalnız space değil; tab/satır sonu gibi tüm whitespace karakterlerini
        // temizlenir. Böylece logdan kopyalanmış çok satırlı hex de okunabilir.
        string normalized = new string(hex.Where(c => !char.IsWhiteSpace(c)).ToArray());

        if (normalized.Length % 2 != 0)
            throw new FormatException("HEX veri çift sayıda karakter içermelidir.");

        var bytes = new byte[normalized.Length / 2];

        for (int i = 0; i < bytes.Length; i++)
        {
            string pair = normalized.Substring(i * 2, 2);

            try
            {
                bytes[i] = Convert.ToByte(pair, 16);
            }
            catch (FormatException ex)
            {
                throw new FormatException($"Geçersiz HEX byte: '{pair}'.", ex);
            }
        }

        return bytes;
    }
}
