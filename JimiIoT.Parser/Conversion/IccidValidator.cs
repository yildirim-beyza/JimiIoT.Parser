namespace JimiIoT.Parser.Conversion;

// ICCID ile ham/alfa-nümerik kimlikleri birbirinden ayırır.
internal static class IccidValidator
{
    internal static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string iccid = value.Trim('\0', '\r', '\n', ' ', '\u00FF');

        // ICCID pratikte 19 veya 20 ondalık hanedir. Sabit uzunluklu cihaz
        // alanlarındaki padding trimlendikten sonra yalnız bu iki biçim kabul edilir.
        if (iccid.Length is not (19 or 20) || !iccid.All(char.IsDigit))
            return null;

        return iccid;
    }
}
