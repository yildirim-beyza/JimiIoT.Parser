namespace JimiIoT.Parser.Protocols;

// 0x94 Info mesajının subtype'a göre değişen içeriğini internal olarak taşır.
internal class InfoMessageData
{
    internal string? Iccid { get; init; }
    internal string? NetworkTechnology { get; init; }
    internal decimal? ExternalVoltageV { get; init; }
}
