namespace JimiIoT.Parser.Framing;

// ============================================================================
// DeviceFrame
// ----------------------------------------------------------------------------
// JIMI paketinin "zarfı" çözüldükten sonraki nötr temsilidir.
// Bu sınıf GPS, VIN veya batarya gibi alanların anlamını bilmez.
// Yalnızca framing katmanının çıkardığı parçaları taşır.
//
// Rehber karşılığı: JIMI/GT06 frame anatomisi ve frame metadata sözleşmesi.
// ============================================================================
internal class DeviceFrame
{
    // 0x7878 kısa pakette length alanı 1 bayt, 0x7979 uzun pakette 2 bayttır.
    public int LengthFieldSize { get; init; }

    // Protokol içindeki Length alanının sayısal değeri.
    public int Length { get; init; }

    // Mesaj tipini belirleyen 1 baytlık protokol numarası (ör. 0x01 login).
    public byte ProtocolNumber { get; init; }

    // Start/length/protocol/serial/CRC/end çıkarıldıktan sonra kalan payload.
    public byte[] InformationContent { get; init; } = Array.Empty<byte>();

    // Mesaj sıra numarası.
    public ushort SerialNumber { get; init; }

    // Cihazın gönderdiği CRC değeri.
    public ushort Checksum { get; init; }
}
