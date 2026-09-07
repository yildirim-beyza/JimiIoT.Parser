namespace JimiIoT.Parser.Protocols;

// ============================================================================
// ProtocolResolver
// ----------------------------------------------------------------------------
// Bu sınıf payload'u PARSE ETMEZ. Yalnızca frame imzasına bakıp hangi teknik
// protokol kolunun kullanılacağını belirler.
//
// JIMI Protocol : 0x7878 veya 0x7979 ile başlar.
// JT/T 808      : 0x7E ile başlar ve 0x7E ile biter.
// ============================================================================
internal sealed class ProtocolResolver
{
    internal ProtocolType Resolve(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 2 &&
            ((data[0] == 0x78 && data[1] == 0x78) ||
             (data[0] == 0x79 && data[1] == 0x79)))
        {
            return ProtocolType.Jimi;
        }

        if (data.Length >= 2 && data[0] == 0x7E && data[^1] == 0x7E)
            return ProtocolType.Jt808;

        throw new FormatException(
            "deviceData bilinen bir JimiIoT protokol frame'i değil (JIMI veya JT/T 808)." );
    }
}
