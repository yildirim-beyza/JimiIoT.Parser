namespace JimiIoT.Parser.Models;

// ============================================================================
// OBDDataModel
// ----------------------------------------------------------------------------
// Generic JIMI 0x8C referans decoder veya shared Jimi JT/T 808 F0 profile
// parserından çözülen normalize araç/motor alanlarını taşır.
// Bilinmeyen/ileride eklenecek key'ler RawFields'ta kaybolmadan saklanır.
// ============================================================================
public class OBDDataModel
{
    public double? OdometerKm { get; init; }
    public double? FuelLevelPercent { get; init; }
    public double? CoolantTempC { get; init; }
    public double? SpeedKmh { get; init; }
    public double? RpmValue { get; init; }
    public double? FuelUsedL { get; init; }
    public double? EngineHours { get; init; }

    public bool? IgnitionOn { get; init; }

    // Ham cihaz wall-clock zamanı; UTC dönüşümü yapılmaz. Property adı mevcut
    // model uyumluluğu için korunur ve Kind değeri Unspecified'dır.
    public DateTime DeviceTimeUtc { get; init; }

    // Örn. "4A" -> VIN. Key'ler parser içinde büyük harfe normalize edilir.
    public IReadOnlyDictionary<string, string> RawFields { get; init; }
        = new Dictionary<string, string>();
}
