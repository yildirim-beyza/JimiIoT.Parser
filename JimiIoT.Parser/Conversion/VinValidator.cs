using System.Text;

namespace JimiIoT.Parser.Conversion;

// VIN doğrulamasını bütün parser yollarında tek noktada tutar.
internal static class VinValidator
{
    internal static string? FromAscii(ReadOnlySpan<byte> data)
        => Normalize(Encoding.ASCII.GetString(data));

    internal static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string vin = value.Trim('\0', '\r', '\n', ' ', '\u00FF');
        if (vin.Length != 17)
            return null;

        // ISO 3779 VIN alfabesi I, O ve Q karakterlerini kullanmaz.
        foreach (char character in vin)
        {
            if (!(char.IsDigit(character) || (character is >= 'A' and <= 'Z')) ||
                character is 'I' or 'O' or 'Q')
            {
                return null;
            }
        }

        return vin;
    }
}
