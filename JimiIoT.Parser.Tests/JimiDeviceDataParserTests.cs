using System.Text;
using JimiIoT.Parser.Models;
using JimiIoT.Parser;

namespace JimiIoT.Parser.Tests;

// Public facade üzerinden kritik uçtan-uca davranışlar.
public class JimiDeviceDataParserTests
{
    [Fact]
    public void GetDeviceInfo_LoginFrame_ExtractsImei()
    {
        // Rehberdeki öğretici login örneği. CRC kendi içinde geçerlidir.
        const string frameHex = "78780D0103534190360660610003C3DF0D0A";
        var parser = new JimiDeviceDataParser();

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Equal("353419036066061", result.ExtractedImei);
    }

    [Fact]
    public void GetDeviceInfo_RealGpsSample_ReturnsGpsAndDate()
    {
        // Traccar topluluk forumunda paylaşılan gerçek saha paketi.
        const string frameHex =
            "78782222150401143726cf0531348f01ba644000148e00e80100bf004d760000000467a0490d0a";

        var parser = new JimiDeviceDataParser();

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.NotNull(result.GpsData);
        Assert.NotNull(result.DateData);
        Assert.Equal(new DateTime(2021, 4, 1, 20, 55, 38, DateTimeKind.Unspecified), result.DateData!.DeviceTimeUtc);
        Assert.Equal(DateTimeKind.Unspecified, result.DateData!.DeviceTimeUtc.Kind);
        Assert.Equal(15, result.GpsData!.SatelliteCount!.Value);
        Assert.Equal(48.394888, result.GpsData.Latitude, precision: 5);
        Assert.Equal(16.106987, result.GpsData.Longitude, precision: 5);
        Assert.Equal(0, result.GpsData.SpeedKph!.Value);
        Assert.Equal(142, result.GpsData.Course!.Value);
        Assert.True(result.GpsData.Valid);
    }

    [Fact]
    public void GetDeviceInfo_Heartbeat_ReturnsVehicleStatusWithoutLeakingDeviceVoltage()
    {
        // status=0b0000_0010 -> ignition açık
        // voltage=0x04D2=1234 exists in the generic heartbeat decoder, but the
        // project public battery outputs are vehicle-side only.
        string frameHex = TestFrameBuilder.BuildShortFrameHex(
            protocolNumber: 0x23,
            informationContent: new byte[] { 0b0000_0010, 0x04, 0xD2 });

        var parser = new JimiDeviceDataParser();

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.NotNull(result.InputData);
        Assert.True(result.InputData!.IsIgnitionOn == true);
        Assert.Equal(1, result.VehicleStatus!.Value);
        Assert.Null(result.BatteryVoltage);
        Assert.Null(result.BatteryLevel);
        Assert.Equal((ushort)1, result.ProtocolSerialNumber!.Value);
        Assert.Null(result.BatteryCurrent);
    }

    [Fact]
    public void GetDeviceInfo_Vl512_DoesNotAssumeGeneric0x8CObdMapping()
    {
        // Generic JIMI/Concox 0x8C referans formatına benzeyen bir payload.
        // VL512'nin native OBD yeteneği doğrulanmış olsa da bu exact key/scale
        // upload haritası model-özel kaynakla doğrulanmadığı için public facade
        // 0x8C içeriğini otomatik olarak VL512 ECU verisi saymamalıdır.
        var info = new List<byte>
        {
            26, 8, 25, 10, 20, 30,
            1
        };
        info.AddRange(Encoding.ASCII.GetBytes(
            "28=03E8,36=013880,4A=1G1JC5444R7252367"));

        string frameHex = TestFrameBuilder.BuildShortFrameHex(0x8C, info.ToArray());
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL512);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Null(result.OBDData);
        Assert.Null(result.VIN);
        Assert.Null(result.VehicleStatus);
        Assert.Null(result.DateData);
    }

    [Fact]
    public void GetDeviceInfo_Vl512_UnverifiedJimiDtc_RemainsNull()
    {
        // 0x65 DTC message ID'si generic JIMI ailesinde bilinir; fakat VL512
        // için payload field map doğrulanmadığı sürece public sonuçta "DTC yok"
        // anlamına gelen boş liste üretmeyiz. Destek/sözleşme bilinmediği için null
        // bırakılır ve doğrulanmamış payload parser'ı çağrılmaz.
        string frameHex = TestFrameBuilder.BuildShortFrameHex(
            protocolNumber: 0x65,
            informationContent: Array.Empty<byte>());

        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL512);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Null(result.DTCFaultCodes);
    }

    [Fact]
    public void GetDeviceInfo_Vl512Context_AllowsGenericJimiLoginPath()
    {
        const string frameHex = "78780D0103534190360660610003C3DF0D0A";
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL512);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Equal("353419036066061", result.ExtractedImei);
    }

    [Fact]
    public void GetDeviceInfo_Vl512Context_AllowsGenericJimiLocationPath()
    {
        const string frameHex =
            "78782222150401143726cf0531348f01ba644000148e00e80100bf004d760000000467a0490d0a";
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL512);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.NotNull(result.GpsData);
        Assert.NotNull(result.DateData);
        Assert.Equal(15, result.GpsData!.SatelliteCount!.Value);
        Assert.True(result.GpsData.Valid);
        Assert.Equal(
            new DateTime(2021, 4, 1, 20, 55, 38, DateTimeKind.Unspecified),
            result.DateData!.DeviceTimeUtc);
    }

    [Fact]
    public void GetDeviceInfo_Vl512Heartbeat013_ManufacturerExample_UsesStatusByte()
    {
        // MANUFACTURER_EXAMPLE: JM-VL512 Communication Protocol V1.0.0,
        // Heartbeat Packet 0x13 example. Exact VL512 evidence confirms 0x13;
        // this project only normalizes the terminal-information status byte.
        const string frameHex = "78780A134004040001000FDCEE0D0A";
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL512);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.NotNull(result.InputData);
        Assert.Equal(false, result.InputData!.IsIgnitionOn);
        Assert.Equal(false, result.InputData.IsCharging);
        Assert.Equal(false, result.InputData.IsEngineBlocked);
        Assert.Equal(0, result.VehicleStatus!.Value);
        Assert.Equal((ushort)0x000F, result.ProtocolSerialNumber!.Value);
        Assert.Null(result.BatteryVoltage);
    }

    [Fact]
    public void GetDeviceInfo_GenericJimiReferenceStatus2_DoesNotLeakIntoVehicleBatteryOutputs()
    {
        string frameHex = TestFrameBuilder.BuildShortFrameHex(
            protocolNumber: 0x36,
            informationContent: new byte[]
            {
                0x00, 0x18, 0x02, 0x04, 0xD2,
                0x00, 0x6A, 0x01, 0x4D
            });
        // MANUAL_DERIVED generic/reference 0x36 TLV fixture. It is not
        // claimed as an exact VL512 V1.0.0 message contract.
        var parser = new JimiDeviceDataParser();

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Null(result.BatteryVoltage);
        Assert.Null(result.BatteryCurrent);
        Assert.Null(result.BatteryLevel);
    }


    [Fact]
    public void GetDeviceInfo_Vl512Info0B_DoesNotExposeUnverifiedNetworkTechnology()
    {
        // MANUAL_DERIVED generic/reference 0x94/0B fixture. VL512 V1.0.0 does
        // not establish subtype 0x0B as a network-technology contract, so the
        // internal generic decoder must not leak a guessed 2G/4G public value.
        string frameHex = TestFrameBuilder.BuildLongFrameHex(
            protocolNumber: 0x94,
            informationContent: new byte[] { 0x0B, 0x01 });
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL512);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Null(result.OperatorCellularStatus);
    }

    [Fact]
    public void GetDeviceInfo_GenericInfo0BVoltage_DoesNotLeakIntoVehicleBatteryOutputs()
    {
        // MANUAL_DERIVED generic/reference 0x94/0B two-byte voltage fixture.
        // The decoder may retain this reference semantic internally, but exact
        // vehicle-battery applicability is not proven for the public aggregate.
        string frameHex = TestFrameBuilder.BuildLongFrameHex(
            protocolNumber: 0x94,
            informationContent: new byte[] { 0x0B, 0x04, 0xD2 });
        var parser = new JimiDeviceDataParser();

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Null(result.BatteryVoltage);
        Assert.Null(result.BatteryCurrent);
        Assert.Null(result.BatteryLevel);
    }

    [Fact]
    public void GetDeviceInfo_Vl512Info0A_ReturnsIccid_FromExactV100Contract()
    {
        var info = new List<byte> { 0x0A };
        info.AddRange(new byte[8]); // IMEI alanı
        info.AddRange(new byte[8]); // IMSI alanı
        info.AddRange(new byte[] { 0x89, 0x86, 0x00, 0x12, 0x34, 0x56, 0x78, 0x90, 0x12, 0x34 });

        // MANUAL_DERIVED from JM-VL512 V1.0.0 Information Transfer 0x94/0A:
        // [type 0A] [IMEI 8B] [IMSI 8B] [ICCID 10B], with 0x7979 framing.
        string frameHex = TestFrameBuilder.BuildLongFrameHex(0x94, info.ToArray());
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL512);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Equal("89860012345678901234", result.CCID);
    }

    [Fact]
    public void GetDeviceInfo_Vl512Info00_ExactContract_IsNotImplementedAsVoltageInCurrentScope()
    {
        // MANUAL_DERIVED from JM-VL512 V1.0.0: 0x94/00 carries external
        // battery voltage (04 9F => 11.83V). The current generic parser does
        // not implement subtype 00 and this consistency turn must not add a
        // VL512-specific production branch; keep the limitation explicit.
        string frameHex = TestFrameBuilder.BuildLongFrameHex(
            protocolNumber: 0x94,
            informationContent: new byte[] { 0x00, 0x04, 0x9F });
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL512);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Null(result.BatteryVoltage);
        Assert.Null(result.BatteryLevel);
    }


    [Fact]
    public void GetDeviceInfo_StringMessage_ReturnsValidatedIccid()
    {
        byte[] data = Encoding.ASCII.GetBytes("<ICCID:89860012345678901234>");
        var info = new List<byte> { (byte)(data.Length + 4), 0x00, 0x00, 0x00, 0x00 };
        info.AddRange(data);

        string frameHex = TestFrameBuilder.BuildShortFrameHex(
            protocolNumber: 0x15,
            informationContent: info.ToArray());
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL512);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Equal("89860012345678901234", result.CCID);
    }

    [Fact]
    public void GetDeviceInfo_StringMessage_With19DigitIccid_ReturnsValidatedIccid()
    {
        byte[] data = Encoding.ASCII.GetBytes("<ICCID:8986001234567890123>");
        var info = new List<byte> { (byte)(data.Length + 4), 0x00, 0x00, 0x00, 0x00 };
        info.AddRange(data);

        string frameHex = TestFrameBuilder.BuildShortFrameHex(
            protocolNumber: 0x15,
            informationContent: info.ToArray());
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL512);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Equal("8986001234567890123", result.CCID);
    }

    [Fact]
    public void GetDeviceInfo_StringMessage_WithTrailingGarbageAfterIccid_ReturnsNull()
    {
        byte[] data = Encoding.ASCII.GetBytes("<ICCID:89860012345678901234>EXTRA");
        var info = new List<byte> { (byte)(data.Length + 4), 0x00, 0x00, 0x00, 0x00 };
        info.AddRange(data);

        string frameHex = TestFrameBuilder.BuildShortFrameHex(
            protocolNumber: 0x15,
            informationContent: info.ToArray());
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL512);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Null(result.CCID);
    }

    [Fact]
    public void GetDeviceInfo_StringMessage_WithMalformedIccid_ReturnsNull()
    {
        byte[] data = Encoding.ASCII.GetBytes("<ICCID:8986001234567890ABCD>");
        var info = new List<byte> { (byte)(data.Length + 4), 0x00, 0x00, 0x00, 0x00 };
        info.AddRange(data);

        string frameHex = TestFrameBuilder.BuildShortFrameHex(
            protocolNumber: 0x15,
            informationContent: info.ToArray());
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL512);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Null(result.CCID);
    }

    [Fact]
    public void GetDeviceInfo_EmptyInput_ThrowsArgumentException()
    {
        var parser = new JimiDeviceDataParser();

        Assert.Throws<ArgumentException>(() => parser.GetDeviceInfo(string.Empty));
    }

    [Theory]
    [InlineData("787")]
    [InlineData("78GG")]
    public void GetDeviceInfo_MalformedHexInput_ThrowsFormatException(string deviceData)
    {
        var parser = new JimiDeviceDataParser();

        Assert.Throws<FormatException>(() => parser.GetDeviceInfo(deviceData));
    }

}
