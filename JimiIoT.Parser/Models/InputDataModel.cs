namespace JimiIoT.Parser.Models;

// Cihazın status/ACC kaynaklarından çıkarılabilen giriş/durum bilgileri.
// Nullable alanlar, protokolde bilgi bulunmaması ile gerçek false değerini ayırır.
public class InputDataModel
{
    public bool? IsIgnitionOn { get; init; }
    public bool? IsCharging { get; init; }
    public bool? IsEngineBlocked { get; init; }
}

