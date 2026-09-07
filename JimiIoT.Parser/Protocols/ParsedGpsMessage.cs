using JimiIoT.Parser.Models;

namespace JimiIoT.Parser.Protocols;

// GPS ve tarih aynı payload'dan tek seferde okunur. Bu internal taşıyıcı,
// public model değildir; yalnızca tekrar parsing yapmamak için kullanılır.
internal class ParsedGpsMessage
{
    internal required GpsDataModel Gps { get; init; }
    internal required DateDataModel Date { get; init; }
}
