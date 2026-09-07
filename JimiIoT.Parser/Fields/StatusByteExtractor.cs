namespace JimiIoT.Parser.Fields;

// ============================================================================
// StatusByteExtractor
// ----------------------------------------------------------------------------
// JIMI status byte içindeki tek-bitlik durumları ayırır.
// Bit haritası referans uygulamadaki decodeStatus() davranışıyla doğrulandı:
//   bit 1 -> ignition / ACC
//   bit 2 -> charging
//   bit 7 -> engine blocked / relay
//
// Bu mantık tek yerde tutulduğu için farklı mesaj parser'larında aynı maske
// tekrar tekrar yazılmaz (SRP / tekrarın azaltılması).
// Rehber karşılığı: JIMI status bitleri ve kontak/charging/blocked çözümü.
// ============================================================================
internal static class StatusByteExtractor
{
    internal static bool IsIgnitionOn(byte statusByte)
        => (statusByte & 0b0000_0010) != 0;

    internal static bool IsCharging(byte statusByte)
        => (statusByte & 0b0000_0100) != 0;

    internal static bool IsEngineBlocked(byte statusByte)
        => (statusByte & 0b1000_0000) != 0;
}
