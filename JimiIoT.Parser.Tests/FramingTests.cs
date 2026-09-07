using System.Text;
using JimiIoT.Parser.Conversion;
using JimiIoT.Parser.Framing;

namespace JimiIoT.Parser.Tests;

// Frame ve CRC katmanına ait iki kritik düşük-seviye test.
public class FramingTests
{
    [Fact]
    public void Crc16X25_MatchesStandardCheckValue()
    {
        byte[] data = Encoding.ASCII.GetBytes("123456789");

        ushort crc = Crc16X25.Compute(data);

        Assert.Equal((ushort)0x906E, crc);
    }

    [Fact]
    public void JimiFrameDecoder_RejectsFrame_WhenCrcIsCorrupted()
    {
        byte[] bytes = HexPayloadReader.ToBytes(
            "78780D0103534190360660610003C3DF0D0A");

        // CRC'nin ilk byte'ını bilerek bozuyoruz.
        bytes[^4] ^= 0x01;

        var decoder = new JimiFrameDecoder();

        bool decoded = decoder.TryDecode(bytes, out _, out _);

        Assert.False(decoded);
    }

    [Fact]
    public void JimiFrameDecoder_DecodesLong7979Frame()
    {
        byte[] content = Enumerable.Range(0, 300).Select(value => (byte)value).ToArray();
        byte[] bytes = Convert.FromHexString(
            TestFrameBuilder.BuildLongFrameHex(0x94, content, serialNumber: 0x1234));

        var decoder = new JimiFrameDecoder();

        bool decoded = decoder.TryDecode(bytes, out DeviceFrame frame, out int consumed);

        Assert.True(decoded);
        Assert.Equal(bytes.Length, consumed);
        Assert.Equal(2, frame.LengthFieldSize);
        Assert.Equal(0x94, frame.ProtocolNumber);
        Assert.Equal((ushort)0x1234, frame.SerialNumber);
        Assert.Equal(content, frame.InformationContent);
    }
}
