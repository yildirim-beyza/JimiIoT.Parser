using System.Text;
using JimiIoT.Parser.Framing;
using JimiIoT.Parser.Protocols;

namespace JimiIoT.Parser.Tests;

// Generic JIMI decoder'daki referans mapping tamamen silinmedi. Bu test,
// mapping'in model desteği iddiasından ayrı olarak internal referans decoder
// seviyesinde çalışmaya devam ettiğini doğrular.
public class JimiProtocolParserReferenceTests
{
    [Fact]
    public void Generic0x8CReferenceDecoder_ParsesKnownKeyValueShape()
    {
        var info = new List<byte>
        {
            26, 8, 25, 10, 20, 30,
            1
        };
        info.AddRange(Encoding.ASCII.GetBytes(
            "28=03E8,36=013880,4A=1G1JC5444R7252367"));

        string frameHex = TestFrameBuilder.BuildShortFrameHex(0x8C, info.ToArray());
        byte[] frameBytes = Convert.FromHexString(frameHex);

        var decoder = new JimiFrameDecoder();
        Assert.True(decoder.TryDecode(frameBytes, out DeviceFrame frame, out int consumed));
        Assert.Equal(frameBytes.Length, consumed);

        var protocolParser = new JimiProtocolParser();
        var obd = protocolParser.ParseObd(frame);

        Assert.Equal(10.0, obd.OdometerKm!.Value, precision: 2);
        Assert.Equal(800.0, obd.RpmValue!.Value, precision: 2);
        Assert.Equal("1G1JC5444R7252367", protocolParser.ParseVin(obd));
    }
}
