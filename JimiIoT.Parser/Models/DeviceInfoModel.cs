namespace JimiIoT.Parser.Models;

// ============================================================================
// DeviceInfoModel
// ----------------------------------------------------------------------------
// Public giriş metodu GetDeviceInfo(...) TEK bir complete frame işler. Frame
// JIMI Protocol veya JT/T 808 olabilir. Mesajlar bütün alanları aynı anda
// taşımaz; bu yüzden bu modeldeki
// alanların çoğu nullable'dır. Hangi protokol mesajı geldiyse yalnızca o
// mesajın sağlayabildiği alanlar doldurulur.
// ============================================================================
public class DeviceInfoModel
{
    public string? ExtractedImei { get; internal set; }

    // Complete frame içinden decoder tarafından okunan protokol sıra numarası.
    // TCP/idempotency politikası bu kütüphanenin dışında olsa da üst katmanın
    // mesaj kimliği oluşturabilmesi için kayıpsız biçimde dışarı taşınır.
    public ushort? ProtocolSerialNumber { get; internal set; }

    public InputDataModel? InputData { get; internal set; }
    public OBDDataModel? OBDData { get; internal set; }
    public string? VIN { get; internal set; }
    public string? CCID { get; internal set; }
    public string? OperatorCellularStatus { get; internal set; }
    public decimal? BatteryVoltage { get; internal set; }
    public decimal? BatteryCurrent { get; internal set; }
    public int? VehicleStatus { get; internal set; }
    public GpsDataModel? GpsData { get; internal set; }
    public DateDataModel? DateData { get; internal set; }
    public BatteryDataModel? BatteryLevel { get; internal set; }
    public List<string>? DTCFaultCodes { get; internal set; }
}
