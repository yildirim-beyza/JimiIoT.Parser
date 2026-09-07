namespace JimiIoT.Parser.Framing;

// JT/T 808 zarfı çözüldükten sonra protokol parser'ına verilen nötr frame modeli.
// Bu sınıf public domain modeli değildir; yalnızca framing -> protocol katmanları
// arasında taşınan internal bir teknik nesnedir.
internal sealed class Jt808Frame
{
    internal required ushort MessageId { get; init; }
    internal required ushort BodyProperties { get; init; }
    internal required byte[] TerminalId { get; init; }
    internal required ushort SerialNumber { get; init; }
    internal required byte[] Body { get; init; }

    internal byte? ProtocolVersion { get; init; }
    internal ushort? TotalPackets { get; init; }
    internal ushort? PacketNumber { get; init; }

    internal bool IsVersioned => (BodyProperties & 0x4000) != 0;
    internal bool IsSubpackaged => (BodyProperties & 0x2000) != 0;
    internal int EncryptionType => (BodyProperties >> 10) & 0x07;
    internal int DeclaredBodyLength => BodyProperties & 0x03FF;
}
