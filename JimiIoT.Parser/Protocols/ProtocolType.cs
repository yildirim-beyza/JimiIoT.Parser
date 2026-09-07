namespace JimiIoT.Parser.Protocols;

// Gelen HEX frame'in hangi ana protokol ailesine ait olduğunu belirtir.
// Public API'nin parçası değildir; yalnızca içeride doğru decoder/parser koluna
// yönlendirme yapmak için kullanılır.
internal enum ProtocolType
{
    Jimi,
    Jt808
}
