using System.Runtime.CompilerServices;

// Production kodundaki yardımcı sınıflar dışarıya public açılmaz.
// Yalnızca test assembly'si internal sınıfları görebilir.
[assembly: InternalsVisibleTo("JimiIoT.Parser.Tests")]
