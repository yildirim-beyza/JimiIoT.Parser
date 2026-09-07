namespace JimiIoT.Parser.Models;

// Cihaz paketinin içindeki zaman damgası. Sunucu receive time ile aynı şey değildir.

// Property adı mevcut public model uyumluluğu için DeviceTimeUtc olarak korunur;
// parser bu değeri UTC'ye dönüştürmez. Timezone protokol/model/cihaz ayarından
// kesinleştirilmediği için DateTime.Kind = Unspecified olarak üretilir.
public class DateDataModel
{
    public DateTime DeviceTimeUtc { get; init; }
}
