namespace JimiIoT.Parser.Conversion;

// ============================================================================
// ScaleConverter
// ----------------------------------------------------------------------------
// Aynı ölçek dönüşümlerini parser'ın farklı yerlerine dağıtmamak için ortak
// dönüşümler burada toplanır. Önceki taslakta bu dosya boştu; refactor ile
// gerçekten kullanılan bir yardımcıya dönüştürüldü.
//
// Rehber karşılığı: doğrulanmış alan ölçekleri ve birim dönüşümleri.
// ============================================================================
internal static class ScaleConverter
{
    // JIMI/GT06 GPS koordinat ölçeği. JT/T 808'de bu değer farklıdır.
    internal static double JimiCoordinate(uint rawValue)
        => rawValue / 1_800_000d;

    // Batarya/voltaj gibi 1/100 ölçekli sabit noktalı değerler.
    internal static decimal Hundredths(int rawValue)
        => rawValue / 100m;

    // JIMI OBD key/value alanlarında kullanılan x0.01 ölçeği.
    internal static double ObdHundredths(int rawValue)
        => rawValue * 0.01d;
}
