namespace JimiIoT.Parser.Models;

// Batarya/voltaj mesajlarından normalize edilen değerler.
// VoltageV decimal tutulur; GetParsedBatteryVoltage dönüş tipi
// de decimal olduğu için gereksiz double<->decimal dönüşümü yapılmaz.
public class BatteryDataModel
{
    public decimal? VoltageV { get; init; }
    public int? LevelPercent { get; init; }
}
