namespace JimiIoT.Parser.Models;

// JIMI veya JT/T 808 mesajlarından çıkarılan normalize konum verisi.
public class GpsDataModel
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    // Bazı mesaj aileleri (özellikle VL502 transparent tail) hız/yön/uydu
    // bilgisini taşımaz. Eksik ölçüm gerçek 0 değeriyle karıştırılmamalıdır.
    public double? SpeedKph { get; init; }
    public int? Course { get; init; }
    public bool Valid { get; init; }
    public int? SatelliteCount { get; init; }
}
