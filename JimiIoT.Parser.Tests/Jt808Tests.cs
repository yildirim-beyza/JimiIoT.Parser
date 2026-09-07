using System.Text;
using JimiIoT.Parser.Framing;
using JimiIoT.Parser.Models;
using JimiIoT.Parser.Protocols;
using JimiIoT.Parser.Protocols.Profiles;

namespace JimiIoT.Parser.Tests;

// JT/T 808 + VL502 V1.1.1 için kritik uçtan-uca senaryolar.
public class Jt808Tests
{
    [Fact]
    public void ProtocolResolver_DistinguishesJimiAndJt808()
    {
        var resolver = new ProtocolResolver();

        Assert.Equal(ProtocolType.Jimi, resolver.Resolve(Convert.FromHexString("78780D01")));
        Assert.Equal(ProtocolType.Jt808, resolver.Resolve(Convert.FromHexString("7E00027E")));
    }

    [Fact]
    public void Jt808FrameDecoder_UnescapesBodyAndValidatesXor()
    {
        // Body içinde hem 0x7E hem 0x7D var. TestFrameBuilder bunları
        // 7D02 / 7D01 biçiminde escape eder; decoder geri açmalıdır.
        byte[] originalBody = { 0x11, 0x7E, 0x22, 0x7D, 0x33 };
        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0F00, originalBody);
        byte[] frameBytes = Convert.FromHexString(frameHex);

        var decoder = new Jt808FrameDecoder();
        bool ok = decoder.TryDecode(frameBytes, out Jt808Frame frame, out int consumed);

        Assert.True(ok);
        Assert.Equal(frameBytes.Length, consumed);
        Assert.Equal((ushort)0x0F00, frame.MessageId);
        Assert.Equal(originalBody, frame.Body);
    }

    [Fact]
    public void Jt808FrameDecoder_RejectsFrame_WhenXorIsCorrupted()
    {
        byte[] frameBytes = Convert.FromHexString(
            TestFrameBuilder.BuildJt808FrameHex(0x0F00, new byte[] { 0x11, 0x22 }));

        // On-wire checksum son 0x7E'den hemen önce bulunur. Escape içermeyen
        // bu fixture'da byte'ı bozmak XOR doğrulamasını geçersiz kılar.
        frameBytes[^2] ^= 0x01;

        var decoder = new Jt808FrameDecoder();

        Assert.False(decoder.TryDecode(frameBytes, out _, out _));
    }

    [Fact]
    public void GetDeviceInfo_RealVl502TransparentFrame_IsAcceptedAndExtractsImei()
    {
        // Traccar forumunda gerçek VL502 cihazından paylaşılmış 0x0900/F0/0x03
        // frame. Bu subtype proje alanlarımızdan birini taşımıyor; testin amacı
        // gerçek cihaz frame'inde 0x7E framing + XOR + VL502 terminal-id kuralını
        // birlikte doğrulamaktır.
        const string realFrame =
            "7e0900001b4f07788f224e0070f02608202132540002030125023832000c000a003add320494a9c00f7e";

        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);
        DeviceInfoModel result = parser.GetDeviceInfo(realFrame);

        Assert.Equal("868935060117262", result.ExtractedImei);
    }

    [Fact]
    public void GetDeviceInfo_Vl502V111Registration_ReturnsImeiVinSerialAndNullCcid()
    {
        // MANUAL_DERIVED: VL502/VG502 V1.1.1 Table 8 registration body.
        // Kamuya açık doğrulanabilir bir VL502 V1.1.x 0x0100 raw saha frame'i
        // bulunamadığı için normative byte contract'tan üretilmiştir.
        byte[] body = BuildV111RegistrationBody(
            terminalModel: "VL502",
            vin: "1G1JC5444R7252367");

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0100, body);
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        // VL502 contract example: 0B3A73CE2FF2 -> first 14 IMEI digits 12345678901234
        // + Luhn check digit 7.
        Assert.Equal("123456789012347", result.ExtractedImei);
        Assert.Null(result.CCID);
        Assert.Equal("1G1JC5444R7252367", result.VIN);
        Assert.Equal((ushort)1, result.ProtocolSerialNumber!.Value);
    }

    [Fact]
    public void GetDeviceInfo_Vl502V111Registration_NumericTerminalModelNeverBecomesCcid()
    {
        // MANUAL_DERIVED regression: V1.1.1 Body[9..28] Terminal Model'dir.
        // Değeri ICCID validator'dan geçebilecek 20 rakam olsa bile CCID
        // üretilmemelidir.
        byte[] body = BuildV111RegistrationBody(
            terminalModel: "89860012345678901234",
            vin: "1G1JC5444R7252367");

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0100, body);
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Equal("123456789012347", result.ExtractedImei);
        Assert.Null(result.CCID);
        Assert.Equal("1G1JC5444R7252367", result.VIN);
    }


    [Fact]
    public void GetDeviceInfo_Vl502ParameterResponseF007_ReturnsValidIccid()
    {
        // MANUAL_DERIVED from VL502/VG502 V1.1.1 0x0104 + F007 contract.
        // Full JT/T 808 length/BCC/escaping are produced by TestFrameBuilder.
        byte[] body = BuildV111ParameterQueryResponseBody(
            (0xF007, Encoding.ASCII.GetBytes(
                "IMEI:123456789012347;IMSI:460001234567890;ICCID:89860012345678901234;")));

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0104, body);
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Equal("89860012345678901234", result.CCID);
    }

    [Fact]
    public void GetDeviceInfo_Vl502ParameterResponseF025_ReturnsVin()
    {
        // MANUFACTURER_CONTRACT_SYNTHETIC: V1.1.1 Table 4 defines F025 as
        // STRING Vehicle VIN and 0x0104 already carries parameter responses.
        const string vin = "1G1JC5444R7252367";
        byte[] body = BuildV111ParameterQueryResponseBody(
            (0xF025, Encoding.ASCII.GetBytes(vin)));

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0104, body);
        DeviceInfoModel result = new JimiDeviceDataParser(JimiDeviceModel.VL502)
            .GetDeviceInfo(frameHex);

        Assert.Equal(vin, result.VIN);
    }

    [Fact]
    public void GetDeviceInfo_Vl502ParameterResponseF007AndF025_ParsesBoth()
    {
        const string vin = "1G1JC5444R7252367";
        byte[] body = BuildV111ParameterQueryResponseBody(
            (0xF007, Encoding.ASCII.GetBytes(
                "IMEI:123456789012347;IMSI:460001234567890;ICCID:89860012345678901234;")),
            (0xF025, Encoding.ASCII.GetBytes(vin)));

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0104, body);
        DeviceInfoModel result = new JimiDeviceDataParser(JimiDeviceModel.VL502)
            .GetDeviceInfo(frameHex);

        Assert.Equal("89860012345678901234", result.CCID);
        Assert.Equal(vin, result.VIN);
    }

    [Fact]
    public void GetDeviceInfo_Vl502ParameterResponseF007_UsesKeyBasedTokenParsing()
    {
        // Token order is intentionally different from the protocol example.
        byte[] body = BuildV111ParameterQueryResponseBody(
            (0xF007, Encoding.ASCII.GetBytes(
                "ICCID:89860012345678901234;IMSI:460001234567890;IMEI:123456789012347;")));

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0104, body);
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Equal("89860012345678901234", result.CCID);
    }

    [Fact]
    public void GetDeviceInfo_Vl502ParameterResponseWithoutF007_ReturnsNullCcid()
    {
        byte[] body = BuildV111ParameterQueryResponseBody(
            (0xF006, Encoding.ASCII.GetBytes("example")));

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0104, body);
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Null(result.CCID);
    }

    [Theory]
    [InlineData("IMEI:123456789012347;IMSI:460001234567890;ICCID:;")]
    [InlineData("IMEI:123456789012347;IMSI:460001234567890;ICCID:8986001234567890ABCD;")]
    public void GetDeviceInfo_Vl502ParameterResponseF007_InvalidIccidReturnsNull(string f007Value)
    {
        byte[] body = BuildV111ParameterQueryResponseBody(
            (0xF007, Encoding.ASCII.GetBytes(f007Value)));

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0104, body);
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Null(result.CCID);
    }

    [Fact]
    public void GetDeviceInfo_Vl502ParameterResponseMalformedItemLength_DoesNotProduceCcid()
    {
        // Declared item length intentionally exceeds the remaining body, while the
        // outer JT/T 808 frame itself remains fully valid.
        var body = new List<byte>();
        AddUInt16(body, 0x1234); // response sequence
        body.Add(0x01);          // parameter count
        AddUInt16(body, 0xF007); // WORD parameter ID (V1.1.1 contract)
        body.Add(0x40);          // declared 64-byte value
        body.AddRange(Encoding.ASCII.GetBytes("ICCID:89860012345678901234;"));

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0104, body.ToArray());
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Null(result.CCID);
    }

    [Theory]
    [InlineData(JimiDeviceModel.Unknown)]
    [InlineData(JimiDeviceModel.VL533)]
    [InlineData(JimiDeviceModel.VL512)]
    public void GetDeviceInfo_UnsupportedSharedProfile_ParameterResponseF007_IsIgnored(
        JimiDeviceModel model)
    {
        byte[] body = BuildV111ParameterQueryResponseBody(
            (0xF007, Encoding.ASCII.GetBytes(
                "IMEI:123456789012347;IMSI:460001234567890;ICCID:89860012345678901234;")));
        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0104, body);
        var parser = new JimiDeviceDataParser(model);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Null(result.CCID);
    }

    [Fact]
    public void GetDeviceInfo_Jt808Location_ReturnsGpsDateInputAndVehicleStatus()
    {
        // MANUAL_DERIVED: V1.1.1 0x0200 fixed body + exact 0x31/length=1
        // satellite additional-information contract.
        var body = new List<byte>();
        AddUInt32(body, 0); // alarm
        AddUInt32(body, 0x00000003); // bit0 ACC on + bit1 located
        AddUInt32(body, 40_765_432); // latitude 40.765432
        AddUInt32(body, 29_987_654); // longitude 29.987654
        AddUInt16(body, 10); // elevation
        AddUInt16(body, 523); // 52.3 km/h
        AddUInt16(body, 142); // course
        body.AddRange(new byte[] { 0x26, 0x08, 0x25, 0x19, 0x40, 0x30 });
        body.AddRange(new byte[] { 0x31, 0x01, 0x0C }); // V1.1.1 satellites extension: 12

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0200, body.ToArray());
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.NotNull(result.GpsData);
        Assert.NotNull(result.DateData);
        Assert.NotNull(result.InputData);
        Assert.Equal(40.765432, result.GpsData!.Latitude, precision: 6);
        Assert.Equal(29.987654, result.GpsData.Longitude, precision: 6);
        Assert.Equal(52.3, result.GpsData.SpeedKph!.Value, precision: 1);
        Assert.Equal(142, result.GpsData.Course!.Value);
        Assert.Equal(12, result.GpsData.SatelliteCount!.Value);
        Assert.True(result.GpsData.Valid);
        Assert.True(result.InputData!.IsIgnitionOn == true);
        Assert.Equal(1, result.VehicleStatus!.Value);
        Assert.Equal(new DateTime(2026, 8, 25, 19, 40, 30, DateTimeKind.Unspecified), result.DateData!.DeviceTimeUtc);
        Assert.Equal(DateTimeKind.Unspecified, result.DateData!.DeviceTimeUtc.Kind);
    }


    [Theory]
    [InlineData(0x2000, "2G")]
    [InlineData(0x2001, "4G")]
    public void GetDeviceInfo_Vl502LocationE8_ReturnsVendorNetworkTechnology(
        int technology,
        string expected)
    {
        byte[] e8Payload = BuildVl502E8Payload((ushort)technology);
        byte[] body = BuildLocationBody(BuildLocationExtension(0xE8, e8Payload));
        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0200, body);
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Equal(expected, result.OperatorCellularStatus);
    }

    [Fact]
    public void GetDeviceInfo_Vl502LocationWithoutE8_ReturnsNullOperatorStatus()
    {
        byte[] body = BuildLocationBody();
        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0200, body);
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Null(result.OperatorCellularStatus);
    }

    [Fact]
    public void GetDeviceInfo_Vl502LocationSignalStrengthOnly_DoesNotInferNetworkTechnology()
    {
        // 0x30 is signal strength, not a 2G/4G technology indicator.
        byte[] body = BuildLocationBody(BuildLocationExtension(0x30, new byte[] { 0x40 }));
        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0200, body);
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Null(result.OperatorCellularStatus);
    }

    [Fact]
    public void GetDeviceInfo_Vl502LocationE8UnknownTechnology_ReturnsNull()
    {
        byte[] body = BuildLocationBody(
            BuildLocationExtension(0xE8, BuildVl502E8Payload(0x2002)));
        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0200, body);
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Null(result.OperatorCellularStatus);
    }

    [Fact]
    public void GetDeviceInfo_Vl502LocationMalformedE8_ReturnsNull()
    {
        // Outer additional-info TLV is valid, but E8 itself is structurally too short.
        byte[] body = BuildLocationBody(
            BuildLocationExtension(0xE8, new byte[] { 0x20, 0x01 }));
        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0200, body);
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Null(result.OperatorCellularStatus);
    }

    [Fact]
    public void GetDeviceInfo_Vl502LocationE8SingleStation_IsStructurallyRejected()
    {
        // V1.1.1 Table 24 states 1 < N < 7; N=1 is therefore not accepted.
        var payload = new List<byte>();
        AddUInt16(payload, 0x2001);
        payload.Add(12);
        AddUInt16(payload, 460);
        AddUInt16(payload, 0);
        payload.Add(1);
        AddUInt16(payload, 0x1234);
        AddUInt32(payload, 0x000ABCDE);
        payload.Add(0x40);

        byte[] body = BuildLocationBody(
            BuildLocationExtension(0xE8, payload.ToArray()));
        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0200, body);
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Null(result.OperatorCellularStatus);
    }

    [Theory]
    [InlineData(JimiDeviceModel.Unknown)]
    [InlineData(JimiDeviceModel.VL533)]
    [InlineData(JimiDeviceModel.VL512)]
    public void GetDeviceInfo_UnsupportedSharedProfile_LocationE8_IsIgnored(
        JimiDeviceModel model)
    {
        byte[] body = BuildLocationBody(
            BuildLocationExtension(0xE8, BuildVl502E8Payload(0x2001)));
        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0200, body);
        var parser = new JimiDeviceDataParser(model);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Null(result.OperatorCellularStatus);
    }


    [Theory]
    [InlineData(0x61)]
    [InlineData(0x69)]
    public void GetDeviceInfo_GenericJt808VoltageExtensions_DoNotLeakIntoVehicleBatteryOutputs(int extensionId)
    {
        // MANUAL_DERIVED generic JT/T 808 reference extension. These helpers are
        // retained internally, but 0x61/0x69 do not have a project-proven
        // vehicle-battery semantic and therefore must not feed public outputs.
        byte[] body = BuildLocationBody(
            BuildLocationExtension((byte)extensionId, new byte[] { 0x04, 0xD2 }));
        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0200, body);
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Null(result.BatteryVoltage);
        Assert.Null(result.BatteryCurrent);
        Assert.Null(result.BatteryLevel);
    }

    [Fact]
    public void GetDeviceInfo_RealVl502ObdServerLog_ReturnsObdVoltageAndGps()
    {
        // Gerçek VL502 cihazından Traccar server loguna düşen 0x0900/F0/0x01
        // OBD data-stream frame'i.
        //
        // Kaynak:
        // Traccar Forum - "Jimi IoT VL501 Protocol Integration"
        //
        // Frame içindeki doğrulanabilir alanlardan bazıları:
        // 0x052C -> fuel used
        // 0x052D -> coolant temperature
        // 0x0530 -> voltage
        // 0x0535 -> speed
        // 0x0536 -> RPM
        // 0x0546 -> accumulated mileage

        const string realFrame =
            "7e090000524f07788ef5930017f02306201047580002010b052c040000a474052d017a052e015005300231740535020000053602000005380200000539020000053d0203de0546040000dc88054504000001b200000001015455ae06cdf1a8fc7e";

        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(realFrame);

        Assert.NotNull(result.OBDData);

        // 0x0546 = 0x0000DC88 = 56456 -> /10 = 5645.6 km
        Assert.Equal(
            5645.6,
            result.OBDData!.OdometerKm!.Value,
            precision: 1);

        // 0x052C = 0x0000A474 = 42100 -> /100 = 421.00 L
        Assert.Equal(
            421.0,
            result.OBDData.FuelUsedL!.Value,
            precision: 1);

        // 0x052D = 0x7A = 122 -> 122 - 40 = 82 °C
        Assert.Equal(
            82.0,
            result.OBDData.CoolantTempC!.Value,
            precision: 1);

        // 0x0535 = 0 -> 0 km/h
        Assert.Equal(
            0.0,
            result.OBDData.SpeedKmh!.Value,
            precision: 1);

        // 0x0536 = 0 -> 0 RPM
        Assert.Equal(
            0.0,
            result.OBDData.RpmValue!.Value,
            precision: 1);

        // 0x0530 = 0x3174 = 12660 mV -> 12.66 V
        Assert.Equal(
            12.66m,
            result.BatteryVoltage!.Value);
        Assert.Null(result.BatteryCurrent);
        Assert.Null(result.BatteryLevel);

        // Transparent tail status = 0x00000001 -> ACC on
        Assert.Equal(
            1,
            result.VehicleStatus!.Value);

        Assert.NotNull(result.GpsData);

        // Tail GPS:
        // latitude  = 0x015455AE / 1_000_000
        // longitude = 0x06CDF1A8 / 1_000_000
        Assert.Equal(
            22.304174,
            result.GpsData!.Latitude,
            precision: 6);

        Assert.Equal(
            114.160040,
            result.GpsData.Longitude,
            precision: 6);

        // F0 event time = 23 06 20 10 47 58
        Assert.Equal(
            new DateTime(
                2023,
                6,
                20,
                10,
                47,
                58,
                DateTimeKind.Unspecified),
            result.DateData!.DeviceTimeUtc);
        Assert.Equal(DateTimeKind.Unspecified, result.DateData!.DeviceTimeUtc.Kind);
    }

    [Theory]
    [InlineData(0x0102, 1)]
    [InlineData(0x0105, 1)]
    [InlineData(0x0127, 1)]
    [InlineData(0x0528, 1)]
    [InlineData(0x052B, 2)]
    [InlineData(0x052C, 1)]
    [InlineData(0x052D, 2)]
    [InlineData(0x0530, 1)]
    [InlineData(0x0703, 1)]
    [InlineData(0x0704, 1)]
    [InlineData(0x0705, 2)]
    [InlineData(0x0535, 1)]
    [InlineData(0x0536, 1)]
    [InlineData(0x0544, 2)]
    [InlineData(0x0546, 1)]
    public void GetDeviceInfo_Vl502ObdKnownFieldWrongLength_PreservesRawWithoutSemanticValue(
        int fieldId,
        int malformedLength)
    {
        // MANUAL_DERIVED_NEGATIVE: exact V1.1.1 known-field contract'tan
        // yalnız hedef record length/value kontrollü biçimde bozulur. Frame
        // length/BCC/escaping TestFrameBuilder tarafından yeniden hesaplanır.
        byte[] value = Enumerable.Repeat((byte)0x01, malformedLength).ToArray();
        string frameHex = BuildSingleObdFieldFrameHex((ushort)fieldId, value);
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.NotNull(result.OBDData);
        Assert.Equal(Convert.ToHexString(value), result.OBDData!.RawFields[fieldId.ToString("X4")]);
        AssertKnownMalformedFieldHasNoSemanticValue((ushort)fieldId, result);
    }


    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(10000, 1000.0)]
    public void GetDeviceInfo_Vl5020703_TotalVoltage_UsesGbTScale(
        int rawValue,
        double expectedVoltage)
    {
        string frameHex = BuildSingleObdFieldFrameHex(0x0703, UInt16Bytes((ushort)rawValue));
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        // 0x0703 is NEV traction-system total voltage, not the conventional
        // vehicle-electrical BatteryVoltage contract exposed by 0x0530.
        Assert.Null(result.BatteryVoltage);
        Assert.Null(result.BatteryCurrent);
        Assert.Null(result.BatteryLevel);
        Assert.Equal(rawValue.ToString("X4"), result.OBDData!.RawFields["0703"]);
    }

    [Theory]
    [InlineData(10001)]
    [InlineData(0xFFFE)]
    [InlineData(0xFFFF)]
    public void GetDeviceInfo_Vl5020703_InvalidOrSentinel_PreservesRawWithoutVoltage(int rawValue)
    {
        string frameHex = BuildSingleObdFieldFrameHex(0x0703, UInt16Bytes((ushort)rawValue));
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Null(result.BatteryVoltage);
        Assert.Null(result.BatteryLevel);
        Assert.Equal(rawValue.ToString("X4"), result.OBDData!.RawFields["0703"]);
    }

    [Fact]
    public void GetDeviceInfo_Vl502Voltage_0530IsNotOverriddenByNev0703()
    {
        string frameHex = BuildObdFieldsFrameHex(
            (0x0530, UInt16Bytes(12660)), // 12.66 V fallback
            (0x0703, UInt16Bytes(4000))); // 400.0 V total voltage
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Equal(12.66m, result.BatteryVoltage!.Value);
        Assert.Equal("3174", result.OBDData!.RawFields["0530"]);
        Assert.Equal("0FA0", result.OBDData.RawFields["0703"]);
    }

    [Fact]
    public void GetDeviceInfo_Vl502Voltage_Invalid0703FallsBackToValid0530()
    {
        string frameHex = BuildObdFieldsFrameHex(
            (0x0530, UInt16Bytes(12660)),
            (0x0703, UInt16Bytes(0xFFFE)));
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Equal(12.66m, result.BatteryVoltage!.Value);
        Assert.Null(result.BatteryLevel);
        Assert.Equal("FFFE", result.OBDData!.RawFields["0703"]);
    }

    [Theory]
    [InlineData(0, -1000.0)]
    [InlineData(9469, -53.1)]
    [InlineData(10000, 0.0)]
    [InlineData(10531, 53.1)]
    [InlineData(20000, 1000.0)]
    public void GetDeviceInfo_Vl5020704_TotalCurrent_UsesGbTScale(
        int rawValue,
        double expectedCurrent)
    {
        string frameHex = BuildSingleObdFieldFrameHex(0x0704, UInt16Bytes((ushort)rawValue));
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Equal((decimal)expectedCurrent, result.BatteryCurrent!.Value);
        Assert.Null(result.BatteryVoltage);
        Assert.Null(result.BatteryLevel);
        Assert.Equal(rawValue.ToString("X4"), result.OBDData!.RawFields["0704"]);
    }

    [Theory]
    [InlineData(20001)]
    [InlineData(0xFFFE)]
    [InlineData(0xFFFF)]
    public void GetDeviceInfo_Vl5020704_InvalidOrSentinel_PreservesRawWithoutCurrent(int rawValue)
    {
        string frameHex = BuildSingleObdFieldFrameHex(0x0704, UInt16Bytes((ushort)rawValue));
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Null(result.BatteryCurrent);
        Assert.Equal(rawValue.ToString("X4"), result.OBDData!.RawFields["0704"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void GetDeviceInfo_Vl5020705_SocCreatesVehicleBatteryLevel(int rawValue)
    {
        string frameHex = BuildSingleObdFieldFrameHex(0x0705, new[] { (byte)rawValue });
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.NotNull(result.BatteryLevel);
        Assert.Equal(rawValue, result.BatteryLevel!.LevelPercent!.Value);
        Assert.Null(result.BatteryLevel.VoltageV);
        Assert.Null(result.BatteryVoltage);
        Assert.Null(result.BatteryCurrent);
        Assert.Equal(rawValue.ToString("X2"), result.OBDData!.RawFields["0705"]);
    }

    [Theory]
    [InlineData(101)]
    [InlineData(0xFE)]
    [InlineData(0xFF)]
    public void GetDeviceInfo_Vl5020705_InvalidOrSentinel_PreservesRawWithoutBatteryLevel(int rawValue)
    {
        string frameHex = BuildSingleObdFieldFrameHex(0x0705, new[] { (byte)rawValue });
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Null(result.BatteryLevel);
        Assert.Equal(rawValue.ToString("X2"), result.OBDData!.RawFields["0705"]);
    }

    [Fact]
    public void GetDeviceInfo_Vl5020703And0705_ProducesCoherentNevBatteryLevel()
    {
        string frameHex = BuildObdFieldsFrameHex(
            (0x0703, UInt16Bytes(4000)),
            (0x0705, new byte[] { 80 }));
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Null(result.BatteryVoltage);
        Assert.NotNull(result.BatteryLevel);
        Assert.Equal(80, result.BatteryLevel!.LevelPercent!.Value);
        Assert.Equal(400.0m, result.BatteryLevel.VoltageV!.Value);
    }

    [Fact]
    public void GetDeviceInfo_Vl5020530And0705_DoesNotMergeDifferentBatteryContexts()
    {
        string frameHex = BuildObdFieldsFrameHex(
            (0x0530, UInt16Bytes(12660)),
            (0x0705, new byte[] { 80 }));
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Equal(12.66m, result.BatteryVoltage!.Value);
        Assert.NotNull(result.BatteryLevel);
        Assert.Equal(80, result.BatteryLevel!.LevelPercent!.Value);
        Assert.Null(result.BatteryLevel.VoltageV);
    }

    [Fact]
    public void GetDeviceInfo_Vl5020703ParsesWithoutPriorF0_07CapabilityState()
    {
        // No F0/07 capability message/state is supplied before this frame.
        string frameHex = BuildSingleObdFieldFrameHex(0x0703, UInt16Bytes(3600));
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Null(result.BatteryVoltage);
        Assert.Equal("0E10", result.OBDData!.RawFields["0703"]);
    }

    [Theory]
    [InlineData(JimiDeviceModel.Unknown)]
    [InlineData(JimiDeviceModel.VL533)]
    [InlineData(JimiDeviceModel.VL512)]
    public void GetDeviceInfo_UnsupportedSharedProfile_VehicleEnergyFields_AreIgnored(JimiDeviceModel model)
    {
        string frameHex = BuildObdFieldsFrameHex(
            (0x0703, UInt16Bytes(4000)),
            (0x0704, UInt16Bytes(10531)),
            (0x0705, new byte[] { 80 }));
        var parser = new JimiDeviceDataParser(model);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Null(result.OBDData);
        Assert.Null(result.BatteryVoltage);
        Assert.Null(result.BatteryCurrent);
        Assert.Null(result.BatteryLevel);
    }

    [Theory]
    [InlineData(0, 0u, false)]
    [InlineData(1, 1u, true)]
    public void GetDeviceInfo_Vl502ObdAcc_UsesOnlyDocumentedZeroOrOneDomain(
        int rawValue,
        uint tailStatus,
        bool expectedIgnition)
    {
        // V1.1.1 exposes ACC twice in a valid F0/01 packet: 0x0522 and the
        // required tail Status DWORD bit 0. Keep the synthetic fixture
        // internally consistent; this test is about the documented 0/1 domain,
        // not about inventing precedence between contradictory sources.
        string frameHex = BuildSingleObdFieldFrameHex(
            0x0522,
            new[] { (byte)rawValue },
            tailStatus);
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Equal(expectedIgnition, result.OBDData!.IgnitionOn);
        Assert.Equal(expectedIgnition, result.InputData?.IsIgnitionOn);
        Assert.Equal(expectedIgnition ? 1 : 0, result.VehicleStatus!.Value);
        Assert.Equal(rawValue.ToString("X2"), result.OBDData.RawFields["0522"]);
    }

    [Theory]
    [InlineData(2, 0u, false)]
    [InlineData(2, 1u, true)]
    [InlineData(0xFF, 0u, false)]
    [InlineData(0xFF, 1u, true)]
    public void GetDeviceInfo_Vl502ObdAcc_Invalid0522_DoesNotSuppressValidTailStatus(
        int rawValue,
        uint tailStatus,
        bool expectedIgnition)
    {
        // 0x0522 only documents 0=OFF and 1=ON. Invalid values must not create
        // an ACC semantic of their own, but the independent required tail Status
        // remains a valid ACC source for the same packet.
        string frameHex = BuildSingleObdFieldFrameHex(
            0x0522,
            new[] { (byte)rawValue },
            tailStatus);
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Equal(expectedIgnition, result.OBDData!.IgnitionOn);
        Assert.Equal(expectedIgnition, result.InputData?.IsIgnitionOn);
        Assert.Equal(expectedIgnition ? 1 : 0, result.VehicleStatus!.Value);
        Assert.Equal(rawValue.ToString("X2"), result.OBDData.RawFields["0522"]);
    }

    [Theory]
    [InlineData(0u, false)]
    [InlineData(1u, true)]
    public void GetDeviceInfo_Vl502ObdAcc_WrongLength_PreservesRawAndIndependentTailStatus(
        uint tailStatus,
        bool expectedIgnition)
    {
        byte[] malformedValue = { 0x01, 0x01 }; // 0x0522 contract length is exactly 1 byte.
        string frameHex = BuildSingleObdFieldFrameHex(0x0522, malformedValue, tailStatus);
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.NotNull(result.OBDData);
        Assert.Equal("0101", result.OBDData!.RawFields["0522"]);
        // The malformed 0x0522 record contributes no semantic ACC value. The
        // final ignition can still come from the independent valid tail status.
        Assert.Equal(expectedIgnition, result.OBDData.IgnitionOn);
        Assert.Equal(expectedIgnition, result.InputData?.IsIgnitionOn);
        Assert.Equal(expectedIgnition ? 1 : 0, result.VehicleStatus!.Value);
    }

    [Theory]
    [InlineData(0x052B, 100, true)]
    [InlineData(0x052B, 101, false)]
    [InlineData(0x0544, 100, true)]
    [InlineData(0x0544, 101, false)]
    [InlineData(0x0544, 255, false)]
    public void GetDeviceInfo_Vl502ObdFuelPercent_RejectsValuesAboveOneHundred(
        int fieldId,
        int rawValue,
        bool expectedValid)
    {
        string frameHex = BuildSingleObdFieldFrameHex((ushort)fieldId, new[] { (byte)rawValue });
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        if (expectedValid)
            Assert.Equal((double)rawValue, result.OBDData!.FuelLevelPercent);
        else
            Assert.Null(result.OBDData!.FuelLevelPercent);

        Assert.Equal(rawValue.ToString("X2"), result.OBDData.RawFields[fieldId.ToString("X4")]);
    }

    [Theory]
    [InlineData(250, 210.0)]
    [InlineData(251, null)]
    public void GetDeviceInfo_Vl502ObdCoolant_EnforcesDocumentedUpperBoundary(
        int rawValue,
        double? expectedCelsius)
    {
        string frameHex = BuildSingleObdFieldFrameHex(0x052D, new[] { (byte)rawValue });
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Equal(expectedCelsius, result.OBDData!.CoolantTempC);
        Assert.Equal(rawValue.ToString("X2"), result.OBDData.RawFields["052D"]);
    }

    [Fact]
    public void GetDeviceInfo_Vl502VinTransparent_ReturnsVin()
    {
        var body = BuildTransparentPrefix(subtype: 0x0B);
        body.Add(0x01); // VIN supported
        body.AddRange(Encoding.ASCII.GetBytes("1G1JC5444R7252367"));

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0900, body.ToArray());
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Equal("1G1JC5444R7252367", result.VIN);
        Assert.Equal(new DateTime(2026, 8, 25, 19, 40, 30, DateTimeKind.Unspecified), result.DateData!.DeviceTimeUtc);
        Assert.Equal(DateTimeKind.Unspecified, result.DateData!.DeviceTimeUtc.Kind);
    }

    [Fact]
    public void GetDeviceInfo_Vl502VinTransparent_ReturnsNullForInvalidVin()
    {
        var body = BuildTransparentPrefix(subtype: 0x0B);
        body.Add(0x01);
        body.AddRange(Encoding.ASCII.GetBytes("1G1JC5444R72523I7"));

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0900, body.ToArray());
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Null(result.VIN);
    }

    [Theory]
    [InlineData(0x02)]
    [InlineData(0x7F)]
    [InlineData(0xFF)]
    public void GetDeviceInfo_Vl502VinTransparent_InvalidSupportFlagDoesNotParse(byte supportFlag)
    {
        var body = BuildTransparentPrefix(subtype: 0x0B);
        body.Add(supportFlag);
        body.AddRange(Encoding.ASCII.GetBytes("1G1JC5444R7252367"));

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0900, body.ToArray());
        DeviceInfoModel result = new JimiDeviceDataParser(JimiDeviceModel.VL502)
            .GetDeviceInfo(frameHex);

        Assert.Null(result.VIN);
    }

    [Fact]
    public void GetDeviceInfo_Vl502VinTransparent_ExtraWireBytesAreRejectedNotTruncated()
    {
        var body = BuildTransparentPrefix(subtype: 0x0B);
        body.Add(0x01);
        body.AddRange(Encoding.ASCII.GetBytes("1G1JC5444R7252367X"));

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0900, body.ToArray());
        DeviceInfoModel result = new JimiDeviceDataParser(JimiDeviceModel.VL502)
            .GetDeviceInfo(frameHex);

        Assert.Null(result.VIN);
    }

    [Fact]
    public void GetDeviceInfo_Vl502BufferedPassengerObd_IsAccepted()
    {
        var body = BuildTransparentPrefix(subtype: 0x01, dataType: 0x01, vehicleType: 0x02);
        body.Add(0x01);
        body.AddRange(UInt16Bytes(0x0536));
        body.Add(0x02);
        body.AddRange(UInt16Bytes(1500));
        AddTransparentTail(body);

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0900, body.ToArray());
        DeviceInfoModel result = new JimiDeviceDataParser(JimiDeviceModel.VL502)
            .GetDeviceInfo(frameHex);

        Assert.Equal(1500, result.OBDData!.RpmValue);
    }

    [Fact]
    public void GetDeviceInfo_Vl502RealtimeCommercialObd_CommercialOnlyIdParses()
    {
        var body = BuildTransparentPrefix(subtype: 0x01, dataType: 0x00, vehicleType: 0x01);
        body.Add(0x01);
        body.AddRange(UInt16Bytes(0x0102));
        body.Add(0x04);
        body.AddRange(UInt32Bytes(12345));
        AddTransparentTail(body);

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0900, body.ToArray());
        DeviceInfoModel result = new JimiDeviceDataParser(JimiDeviceModel.VL502)
            .GetDeviceInfo(frameHex);

        Assert.Equal(1234.5d, result.OBDData!.OdometerKm);
    }

    [Fact]
    public void GetDeviceInfo_Vl502BufferedCommercialObd_CommercialFuelAndEngineHoursParse()
    {
        var body = BuildTransparentPrefix(subtype: 0x01, dataType: 0x01, vehicleType: 0x01);
        body.Add(0x02);
        body.AddRange(UInt16Bytes(0x0105));
        body.Add(0x04);
        body.AddRange(UInt32Bytes(1234));
        body.AddRange(UInt16Bytes(0x0127));
        body.Add(0x04);
        body.AddRange(UInt32Bytes(7200));
        AddTransparentTail(body);

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0900, body.ToArray());
        DeviceInfoModel result = new JimiDeviceDataParser(JimiDeviceModel.VL502)
            .GetDeviceInfo(frameHex);

        Assert.Equal(12.34d, result.OBDData!.FuelUsedL);
        Assert.Equal(2d, result.OBDData.EngineHours);
    }

    [Fact]
    public void GetDeviceInfo_Vl502PassengerObd_CommercialOnlyIdIsNotSemanticallyMapped()
    {
        var body = BuildTransparentPrefix(subtype: 0x01, vehicleType: 0x02);
        body.Add(0x01);
        body.AddRange(UInt16Bytes(0x0102));
        body.Add(0x04);
        body.AddRange(UInt32Bytes(12345));
        AddTransparentTail(body);

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0900, body.ToArray());
        DeviceInfoModel result = new JimiDeviceDataParser(JimiDeviceModel.VL502)
            .GetDeviceInfo(frameHex);

        Assert.Null(result.OBDData!.OdometerKm);
        Assert.Equal("00003039", result.OBDData.RawFields["0102"]);
    }

    [Fact]
    public void GetDeviceInfo_Vl502CommercialObd_PassengerOnlyFuelPercentIsNotSemanticallyMapped()
    {
        var body = BuildTransparentPrefix(subtype: 0x01, vehicleType: 0x01);
        body.Add(0x01);
        body.AddRange(UInt16Bytes(0x052B));
        body.Add(0x01);
        body.Add(75);
        AddTransparentTail(body);

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0900, body.ToArray());
        DeviceInfoModel result = new JimiDeviceDataParser(JimiDeviceModel.VL502)
            .GetDeviceInfo(frameHex);

        Assert.Null(result.OBDData!.FuelLevelPercent);
        Assert.Equal("4B", result.OBDData.RawFields["052B"]);
    }

    [Theory]
    [InlineData(0x01)]
    [InlineData(0x02)]
    public void GetDeviceInfo_Vl502Obd052C_ContextIsValidatedWhileSharedNormalizationIsPreserved(byte vehicleType)
    {
        // 052C has different source labels in the commercial/passenger tables,
        // but both normalize x/100 into the existing FuelUsedL public property.
        var body = BuildTransparentPrefix(subtype: 0x01, vehicleType: vehicleType);
        body.Add(0x01);
        body.AddRange(UInt16Bytes(0x052C));
        body.Add(0x04);
        body.AddRange(UInt32Bytes(1234));
        AddTransparentTail(body);

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0900, body.ToArray());
        DeviceInfoModel result = new JimiDeviceDataParser(JimiDeviceModel.VL502)
            .GetDeviceInfo(frameHex);

        Assert.Equal(12.34d, result.OBDData!.FuelUsedL);
    }

    [Theory]
    [InlineData(0x02, 0x02)] // invalid DataType
    [InlineData(0xFF, 0x02)]
    [InlineData(0x00, 0x00)] // invalid VehicleType
    [InlineData(0x00, 0x03)]
    public void GetDeviceInfo_Vl502F0_InvalidContextThrows(byte dataType, byte vehicleType)
    {
        var body = BuildTransparentPrefix(subtype: 0x01, dataType: dataType, vehicleType: vehicleType);
        body.Add(0x00);
        AddTransparentTail(body);
        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0900, body.ToArray());

        Assert.Throws<InvalidOperationException>(() =>
            new JimiDeviceDataParser(JimiDeviceModel.VL502).GetDeviceInfo(frameHex));
    }

    [Fact]
    public void GetDeviceInfo_Vl502F0_MissingVehicleTypeAndSubtypeThrows()
    {
        byte[] body = { 0xF0, 0x26, 0x08, 0x25, 0x19, 0x40, 0x30, 0x00 };
        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0900, body);

        Assert.Throws<InvalidOperationException>(() =>
            new JimiDeviceDataParser(JimiDeviceModel.VL502).GetDeviceInfo(frameHex));
    }

    [Fact]
    public void GetDeviceInfo_Vl502ObdTrailingGarbageAfterRequiredTailThrows()
    {
        var body = BuildTransparentPrefix(subtype: 0x01);
        body.Add(0x01);
        body.AddRange(UInt16Bytes(0x0536));
        body.Add(0x02);
        body.AddRange(UInt16Bytes(900));
        AddTransparentTail(body);
        body.Add(0xAA);

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0900, body.ToArray());

        Assert.Throws<InvalidOperationException>(() =>
            new JimiDeviceDataParser(JimiDeviceModel.VL502).GetDeviceInfo(frameHex));
    }

    [Fact]
    public void GetDeviceInfo_Vl502ObdRequiredTailStatusOff_ReturnsVehicleStatusOff()
    {
        var body = BuildTransparentPrefix(subtype: 0x01);
        body.Add(0x01);
        body.AddRange(UInt16Bytes(0x0536));
        body.Add(0x02);
        body.AddRange(UInt16Bytes(900));
        AddTransparentTail(body, status: 0x00000002); // located, ACC off

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0900, body.ToArray());
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.NotNull(result.OBDData);
        Assert.Equal(900, result.OBDData!.RpmValue);
        Assert.False(result.OBDData.IgnitionOn);
        Assert.False(result.InputData!.IsIgnitionOn);
        Assert.Equal(0, result.VehicleStatus!.Value);
    }

    [Fact]
    public void GetDeviceInfo_Vl502ObdUnknownField_DoesNotBlockKnownField()
    {
        var body = BuildTransparentPrefix(subtype: 0x01);
        body.Add(0x02);
        body.AddRange(UInt16Bytes(0x9999));
        body.Add(0x01);
        body.Add(0xAB);
        body.AddRange(UInt16Bytes(0x0536));
        body.Add(0x02);
        body.AddRange(UInt16Bytes(1200));
        body.AddRange(UInt32Bytes(0x00000003));
        body.AddRange(UInt32Bytes(40_000_000));
        body.AddRange(UInt32Bytes(29_000_000));

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0900, body.ToArray());
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Equal(1200, result.OBDData!.RpmValue);
        Assert.Equal("AB", result.OBDData.RawFields["9999"]);
        Assert.Equal(1, result.VehicleStatus!.Value);
    }

    [Fact]
    public void GetDeviceInfo_Vl502DtcUnknownInnerLayout_IsSafelySkipped()
    {
        var body = BuildTransparentPrefix(subtype: 0x02);
        body.AddRange(UInt16Bytes(1));
        body.AddRange(UInt32Bytes(1));
        body.AddRange(UInt16Bytes(1));
        body.AddRange(UInt32Bytes(0x00001457));
        body.AddRange(UInt32Bytes(0));
        body.AddRange(UInt16Bytes(7));
        body.AddRange(Encoding.ASCII.GetBytes("P1457!"));
        AddTransparentTail(body);

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0900, body.ToArray());
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.NotNull(result.DTCFaultCodes);
        Assert.Empty(result.DTCFaultCodes!);
    }

    [Fact]
    public void GetDeviceInfo_TraccarVl502DtcRegressionFixture_ReturnsP1457()
    {
        // Traccar'ın "Decode VL502 DTCs" commit'inde regression testi olarak
        // kullanılan VL502 0x0900/F0/0x02 fixture'ı.
        //
        // Traccar fixture'ındaki normalize edilmiş biçim:
        //
        // 7e090000344f07788ef87d0138f02305151230460102020001
        // ffffffff000100001457000000020006503134353700000c000a
        // 029dc63004b99a98230515132726787e
        //
        // Terminal ID'nin son byte'ı literal 0x7D'dir.
        // JT/T 808 wire formatında 0x7D -> 0x7D 0x01 escape edildiği için
        // bizim strict decoder'a aşağıdaki on-wire biçim verilir.
        //
        // Dikkat:
        // ... EF87D 01 01 38 ...
        //          ^^^^^
        //          7D'nin escape edilmiş biçimi
        //               ^^
        //               original serial-number'ın ilk 01 byte'ı

        const string regressionFrameOnWire =
            "7e090000344f07788ef87d010138f02305151230460102020001ffffffff000100001457000000020006503134353700000c000a029dc63004b99a98230515132726787e";

        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result =
            parser.GetDeviceInfo(regressionFrameOnWire);

        Assert.NotNull(result.DTCFaultCodes);

        Assert.Single(result.DTCFaultCodes!);

        Assert.Equal(
            "P1457",
            result.DTCFaultCodes![0]);
    }
    [Fact]
    public void GetDeviceInfo_Vl502NormativeNoActiveDtc_ReturnsEmptyList()
    {
        // MANUFACTURER_CONTRACT_SYNTHETIC: V1.1.1 normative no-active-DTC tuple:
        // systemCount=1, systemId=FFFFFFFF, faultCount=0.
        // Public gerçek no-fault VL502 raw trace bulunamadığı için exact
        // manufacturer contract'tan üretilmiştir.
        var body = BuildTransparentPrefix(subtype: 0x02);
        body.AddRange(UInt16Bytes(1));
        body.AddRange(UInt32Bytes(0xFFFFFFFF));
        body.AddRange(UInt16Bytes(0));
        AddTransparentTail(body);

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0900, body.ToArray());
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.NotNull(result.DTCFaultCodes);
        Assert.Empty(result.DTCFaultCodes!);
    }

    [Fact]
    public void GetDeviceInfo_Vl502DtcObservedLayout_MultiSystemMultiDtcParses()
    {
        // SYNTHETIC derived from the OPEN_SOURCE_FIXTURE P1457 record shape.
        // This verifies looping/length handling, not a manufacturer-certified
        // generic ARM record layout.
        var body = BuildTransparentPrefix(subtype: 0x02, vehicleType: 0x02);
        body.AddRange(UInt16Bytes(2));

        body.AddRange(UInt32Bytes(0xFFFFFFFF));
        body.AddRange(UInt16Bytes(2));
        AddObservedP1457DtcRecord(body);
        AddObservedP1457DtcRecord(body);

        body.AddRange(UInt32Bytes(0x00000001));
        body.AddRange(UInt16Bytes(1));
        AddObservedP1457DtcRecord(body);
        AddTransparentTail(body);

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0900, body.ToArray());
        DeviceInfoModel result = new JimiDeviceDataParser(JimiDeviceModel.VL502)
            .GetDeviceInfo(frameHex);

        Assert.Equal(new[] { "P1457", "P1457", "P1457" }, result.DTCFaultCodes!.ToArray());
    }

    [Fact]
    public void GetDeviceInfo_Vl502DtcJ1939ConceptualBytes_AreNotDecodedWithoutArmRecordContract()
    {
        // MANUFACTURER_CONTRACT_SYNTHETIC at the 4-byte conceptual level only.
        // V1.1.1 Appendix 10.3 describes J1939 in four bytes, but does not map
        // those four bytes into the 16-byte F0/02 record. Safe result is empty.
        var body = BuildTransparentPrefix(subtype: 0x02, vehicleType: 0x01);
        body.AddRange(UInt16Bytes(1));
        body.AddRange(UInt32Bytes(0x00000000));
        body.AddRange(UInt16Bytes(1));
        body.AddRange(new byte[]
        {
            0x34, 0x12, 0x05, 0x01, // conceptual J1939 DTC bytes
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00
        });
        AddTransparentTail(body);

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0900, body.ToArray());
        DeviceInfoModel result = new JimiDeviceDataParser(JimiDeviceModel.VL502)
            .GetDeviceInfo(frameHex);

        Assert.NotNull(result.DTCFaultCodes);
        Assert.Empty(result.DTCFaultCodes!);
    }

    [Fact]
    public void GetDeviceInfo_Vl502DtcTruncatedRecordThrows()
    {
        var body = BuildTransparentPrefix(subtype: 0x02);
        body.AddRange(UInt16Bytes(1));
        body.AddRange(UInt32Bytes(1));
        body.AddRange(UInt16Bytes(2)); // claims two records
        AddObservedP1457DtcRecord(body);
        AddTransparentTail(body); // not enough bytes for record #2

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0900, body.ToArray());

        Assert.Throws<InvalidOperationException>(() =>
            new JimiDeviceDataParser(JimiDeviceModel.VL502).GetDeviceInfo(frameHex));
    }


    [Fact]
    public void GetDeviceInfo_Jt808LocationWithoutSatelliteExtension_LeavesUnknownFieldsNull()
    {
        var body = new List<byte>();
        AddUInt32(body, 0); // alarm
        AddUInt32(body, 0x00000003); // ACC on + valid location
        AddUInt32(body, 40_765_432);
        AddUInt32(body, 29_987_654);
        AddUInt16(body, 10); // altitude
        AddUInt16(body, 0);  // speed gerçekten 0
        AddUInt16(body, 0);  // course gerçekten 0
        body.AddRange(new byte[] { 0x26, 0x08, 0x25, 0x19, 0x40, 0x30 });

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0200, body.ToArray());
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Equal(0d, result.GpsData!.SpeedKph!.Value);
        Assert.Equal(0, result.GpsData.Course!.Value);
        Assert.Null(result.GpsData.SatelliteCount);
        Assert.Null(result.InputData!.IsCharging);
    }

    [Fact]
    public void GetDeviceInfo_Vl502ObdTransparentTail_DoesNotInventGpsMeasurements()
    {
        var body = BuildTransparentPrefix(subtype: 0x01);
        body.Add(0x01); // one field
        body.AddRange(UInt16Bytes(0x0536)); // RPM only; no speed field
        body.Add(0x02);
        body.AddRange(UInt16Bytes(900));
        body.AddRange(UInt32Bytes(0x00000003)); // ACC on + valid location
        body.AddRange(UInt32Bytes(40_000_000));
        body.AddRange(UInt32Bytes(29_000_000));

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0900, body.ToArray());
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.NotNull(result.GpsData);
        Assert.Null(result.GpsData!.SpeedKph);
        Assert.Null(result.GpsData.Course);
        Assert.Null(result.GpsData.SatelliteCount);
        Assert.Null(result.InputData!.IsCharging);
    }

    [Fact]
    public void GetDeviceInfo_Vl502ObdMissingRequiredTail_Throws()
    {
        var body = BuildTransparentPrefix(subtype: 0x01);
        body.Add(0x01);
        body.AddRange(UInt16Bytes(0x0522));
        body.Add(0x01);
        body.Add(0x01); // ACC on

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0900, body.ToArray());
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        Assert.Throws<InvalidOperationException>(() => parser.GetDeviceInfo(frameHex));
    }

    [Fact]
    public void GetDeviceInfo_Vl502ObdFeec_PreservesRawButDoesNotInventVinAlias()
    {
        const string vin = "1G1JC5444R7252367";
        var body = BuildTransparentPrefix(subtype: 0x01);
        body.Add(0x01);
        body.AddRange(UInt16Bytes(0xFEEC));
        body.Add((byte)vin.Length);
        body.AddRange(Encoding.ASCII.GetBytes(vin));
        AddTransparentTail(body);

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0900, body.ToArray());
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Null(result.VIN);
        Assert.Equal(Convert.ToHexString(Encoding.ASCII.GetBytes(vin)), result.OBDData!.RawFields["FEEC"]);
        Assert.False(result.OBDData.RawFields.ContainsKey("4A"));
    }

    [Fact]
    public void GetDeviceInfo_Vl502ObdFeecInvalidVin_ReturnsNullButPreservesRawHex()
    {
        const string invalidVin = "1G1JC5444R72523I7"; // I is not valid in ISO 3779 VIN alphabet
        var body = BuildTransparentPrefix(subtype: 0x01);
        body.Add(0x01);
        body.AddRange(UInt16Bytes(0xFEEC));
        body.Add((byte)invalidVin.Length);
        body.AddRange(Encoding.ASCII.GetBytes(invalidVin));
        AddTransparentTail(body);

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0900, body.ToArray());
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Null(result.VIN);
        Assert.Equal(Convert.ToHexString(Encoding.ASCII.GetBytes(invalidVin)), result.OBDData!.RawFields["FEEC"]);
        Assert.False(result.OBDData.RawFields.ContainsKey("4A"));
    }

    [Fact]
    public void GetDeviceInfo_Jt808Location_WithMalformedSatelliteLength_LeavesSatelliteUnknown()
    {
        // MANUAL_DERIVED_NEGATIVE: V1.1.1 0x31 requires length exactly 1.
        var body = new List<byte>();
        AddUInt32(body, 0); // alarm
        AddUInt32(body, 0x00000003); // ACC on + valid location
        AddUInt32(body, 40_765_432);
        AddUInt32(body, 29_987_654);
        AddUInt16(body, 10);
        AddUInt16(body, 523);
        AddUInt16(body, 142);
        body.AddRange(new byte[] { 0x26, 0x08, 0x25, 0x19, 0x40, 0x30 });
        body.AddRange(new byte[] { 0x31, 0x02, 0x0C, 0xFF });

        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0200, body.ToArray());
        var parser = new JimiDeviceDataParser();

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Null(result.GpsData!.SatelliteCount);
    }

    [Fact]
    public void GetDeviceInfo_Vl502BcdLookingTerminalSn_StillUsesVl502BinaryRule()
    {
        // 12 34 56 78 90 12 contains only decimal-looking nibbles, but under
        // VL502 V1.1.1 altında da 6-byte binary Terminal SN olarak yorumlanır.
        string frameHex = TestFrameBuilder.BuildJt808FrameHex(
            0x0002,
            Array.Empty<byte>(),
            terminalIdHex: "123456789012");
        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);

        DeviceInfoModel result = parser.GetDeviceInfo(frameHex);

        Assert.Equal("200159983411382", result.ExtractedImei);
    }

    [Fact]
    public void Jt808FrameDecoder_Accepts2019VersionedHeader_ButVl502ProfileRejectsIt()
    {
        string frameHex = TestFrameBuilder.BuildJt808VersionedFrameHex(
            0x0002,
            Array.Empty<byte>(),
            serialNumber: 0x1234);
        byte[] frameBytes = Convert.FromHexString(frameHex);

        var decoder = new Jt808FrameDecoder();
        Assert.True(decoder.TryDecode(frameBytes, out Jt808Frame frame, out _));
        Assert.True(frame.IsVersioned);
        Assert.Equal(10, frame.TerminalId.Length);
        Assert.Equal((ushort)0x1234, frame.SerialNumber);

        var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);
        Assert.Throws<NotSupportedException>(() => parser.GetDeviceInfo(frameHex));
    }

    [Fact]
    public void GetDeviceInfo_Jt808Subpackage_IsDeliberatelyRejected()
    {
        string frameHex = TestFrameBuilder.BuildJt808FrameHex(
            0x0002,
            Array.Empty<byte>(),
            subpackaged: true,
            totalPackets: 2,
            packetNumber: 1);
        var parser = new JimiDeviceDataParser();

        Assert.Throws<NotSupportedException>(() => parser.GetDeviceInfo(frameHex));
    }

    [Fact]
    public void GetDeviceInfo_EncryptedJt808Body_IsDeliberatelyRejected()
    {
        string frameHex = TestFrameBuilder.BuildJt808FrameHex(
            0x0002,
            Array.Empty<byte>(),
            encryptionType: 1);
        var parser = new JimiDeviceDataParser();

        Assert.Throws<NotSupportedException>(() => parser.GetDeviceInfo(frameHex));
    }

    [Fact]
    public void GetDeviceInfo_Jt808FrameMissingFinalDelimiter_IsRejected()
    {
        string validFrame = TestFrameBuilder.BuildJt808FrameHex(0x0002, Array.Empty<byte>());
        string missingFinalDelimiter = validFrame[..^2];
        var parser = new JimiDeviceDataParser();

        Assert.Throws<FormatException>(() => parser.GetDeviceInfo(missingFinalDelimiter));
    }

    [Theory]
    [InlineData(JimiDeviceModel.VL533)]
    [InlineData(JimiDeviceModel.VL512)]
    public void GetDeviceInfo_UnsupportedSharedProfile_DoesNotUseJimiF0Mapping(
        JimiDeviceModel model)
    {
        // Bu frame gerçek bir VL502 F0/01 OBD örneğidir. Testte aynı byte'ları
        // VL533/VL512 model context'i ile vererek profile izolasyonunu kontrol
        // ediyoruz: framing geçerli olsa bile shared Jimi field-ID/scale tablosu
        // doğrulanmamış modellere uygulanmamalıdır.
        const string vl502ObdFrame =
            "7e090000524f07788ef5930017f02306201047580002010b052c040000a474052d017a052e015005300231740535020000053602000005380200000539020000053d0203de0546040000dc88054504000001b200000001015455ae06cdf1a8fc7e";

        var parser = new JimiDeviceDataParser(model);

        DeviceInfoModel result = parser.GetDeviceInfo(vl502ObdFrame);

        Assert.Null(result.OBDData);
        Assert.Null(result.VIN);
        Assert.Null(result.DTCFaultCodes);
        Assert.Null(result.BatteryVoltage);
    }

    [Fact]
    public void GetDeviceInfo_UnknownJt808Model_DoesNotUseVl502F0Mapping()
    {
        // Parametresiz parser güvenli generic modda çalışır. Model bilinmiyorsa
        // vendor-specific F0 payload çözülmez.
        const string vl502ObdFrame =
            "7e090000524f07788ef5930017f02306201047580002010b052c040000a474052d017a052e015005300231740535020000053602000005380200000539020000053d0203de0546040000dc88054504000001b200000001015455ae06cdf1a8fc7e";

        var parser = new JimiDeviceDataParser();

        DeviceInfoModel result = parser.GetDeviceInfo(vl502ObdFrame);

        Assert.Null(result.OBDData);
        Assert.Null(result.ExtractedImei);
    }

    [Theory]
    [InlineData(JimiDeviceModel.VL502, true)]
    [InlineData(JimiDeviceModel.VG502, true)]
    [InlineData(JimiDeviceModel.VL533, false)]
    [InlineData(JimiDeviceModel.VL512, false)]
    [InlineData(JimiDeviceModel.Unknown, false)]
    public void SharedJimiVehicleProfile_Eligibility_IsExplicit(
        JimiDeviceModel model,
        bool expected)
    {
        Assert.Equal(expected, JimiJt808VehicleDataProfilePolicy.Supports(model));
    }

    [Fact]
    public void GetDeviceInfo_Vg502SharedContract_F007Parses()
    {
        // MANUFACTURER-CONTRACT/SYNTHETIC: same V1.1.1 F007 packet contract,
        // intentionally not presented as a real VG502 device trace.
        byte[] body = BuildV111ParameterQueryResponseBody(
            (0xF007, Encoding.ASCII.GetBytes(
                "IMEI:123456789012347;IMSI:460001234567890;ICCID:89860012345678901234;")));
        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0104, body);

        DeviceInfoModel result = new JimiDeviceDataParser(JimiDeviceModel.VG502)
            .GetDeviceInfo(frameHex);

        Assert.Equal("89860012345678901234", result.CCID);
    }

    [Fact]
    public void GetDeviceInfo_Vg502SharedContract_E8GsmParses()
    {
        byte[] body = BuildLocationBody(
            BuildLocationExtension(0xE8, BuildVl502E8Payload(0x2000)));
        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0200, body);

        DeviceInfoModel result = new JimiDeviceDataParser(JimiDeviceModel.VG502)
            .GetDeviceInfo(frameHex);

        Assert.Equal("2G", result.OperatorCellularStatus);
    }

    [Fact]
    public void GetDeviceInfo_Vg502SharedContract_ObdParsesSameFieldsAsVl502()
    {
        // SHARED-CONTRACT fixture: protocol-defined F0/01 payload reused under
        // VG502 context to verify shared parser routing, not claimed as VG502 raw log.
        string frameHex = BuildObdFieldsFrameHex(
            (0x0536, UInt16Bytes(1500)),
            (0x0535, UInt16Bytes(523)),
            (0x052D, new byte[] { 100 }),
            (0x0530, UInt16Bytes(12660)));

        DeviceInfoModel vl502 = new JimiDeviceDataParser(JimiDeviceModel.VL502)
            .GetDeviceInfo(frameHex);
        DeviceInfoModel vg502 = new JimiDeviceDataParser(JimiDeviceModel.VG502)
            .GetDeviceInfo(frameHex);

        Assert.Equal(vl502.OBDData!.RpmValue, vg502.OBDData!.RpmValue);
        Assert.Equal(vl502.OBDData.SpeedKmh, vg502.OBDData.SpeedKmh);
        Assert.Equal(vl502.OBDData.CoolantTempC, vg502.OBDData.CoolantTempC);
        Assert.Equal(vl502.BatteryVoltage, vg502.BatteryVoltage);
        Assert.Equal(12.66m, vg502.BatteryVoltage!.Value);
    }

    [Fact]
    public void GetDeviceInfo_Vg502SharedContract_VinSupportFlagZeroProducesNoVin()
    {
        var body = BuildTransparentPrefix(subtype: 0x0B);
        body.Add(0x00); // explicitly unsupported
        body.AddRange(Encoding.ASCII.GetBytes("1G1JC5444R7252367"));
        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0900, body.ToArray());

        DeviceInfoModel result = new JimiDeviceDataParser(JimiDeviceModel.VG502)
            .GetDeviceInfo(frameHex);

        Assert.Null(result.VIN);
    }

    [Fact]
    public void GetDeviceInfo_Vg502SharedContract_VinSupportFlagOneParsesVin()
    {
        var body = BuildTransparentPrefix(subtype: 0x0B);
        body.Add(0x01);
        body.AddRange(Encoding.ASCII.GetBytes("1G1JC5444R7252367"));
        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0900, body.ToArray());

        DeviceInfoModel result = new JimiDeviceDataParser(JimiDeviceModel.VG502)
            .GetDeviceInfo(frameHex);

        Assert.Equal("1G1JC5444R7252367", result.VIN);
    }

    [Fact]
    public void GetDeviceInfo_Vg502SharedContract_NoActiveDtcParsesEmptyList()
    {
        var body = BuildTransparentPrefix(subtype: 0x02);
        body.AddRange(UInt16Bytes(1));
        body.AddRange(UInt32Bytes(0xFFFFFFFF));
        body.AddRange(UInt16Bytes(0));
        AddTransparentTail(body);
        string frameHex = TestFrameBuilder.BuildJt808FrameHex(0x0900, body.ToArray());

        DeviceInfoModel result = new JimiDeviceDataParser(JimiDeviceModel.VG502)
            .GetDeviceInfo(frameHex);

        Assert.NotNull(result.DTCFaultCodes);
        Assert.Empty(result.DTCFaultCodes!);
    }

    [Fact]
    public void GetDeviceInfo_Vg502SharedContract_0530VehicleVoltageParses()
    {
        string frameHex = BuildSingleObdFieldFrameHex(0x0530, UInt16Bytes(12660));

        DeviceInfoModel result = new JimiDeviceDataParser(JimiDeviceModel.VG502)
            .GetDeviceInfo(frameHex);

        Assert.Equal(12.66m, result.BatteryVoltage!.Value);
        Assert.Null(result.BatteryCurrent);
        Assert.Null(result.BatteryLevel);
    }

    private static List<byte> BuildTransparentPrefix(
        byte subtype,
        byte dataType = 0x00,
        byte vehicleType = 0x02)
    {
        return new List<byte>
        {
            0xF0,
            0x26, 0x08, 0x25, 0x19, 0x40, 0x30, // event time
            dataType,
            vehicleType,
            subtype
        };
    }

    private static string BuildSingleObdFieldFrameHex(
        ushort id,
        byte[] value,
        uint tailStatus = 0)
        => BuildObdFieldsFrameHexWithTailStatus(tailStatus, (id, value));


    private static string BuildObdFieldsFrameHex(params (ushort Id, byte[] Value)[] fields)
        => BuildObdFieldsFrameHexWithTailStatus(0, fields);

    private static string BuildObdFieldsFrameHexWithTailStatus(
        uint tailStatus,
        params (ushort Id, byte[] Value)[] fields)
    {
        if (fields.Length > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(fields));

        var body = BuildTransparentPrefix(subtype: 0x01);
        body.Add((byte)fields.Length);
        foreach ((ushort id, byte[] value) in fields)
        {
            body.AddRange(UInt16Bytes(id));
            body.Add((byte)value.Length);
            body.AddRange(value);
        }

        AddTransparentTail(body, status: tailStatus);

        return TestFrameBuilder.BuildJt808FrameHex(0x0900, body.ToArray());
    }

    private static void AddTransparentTail(
        List<byte> body,
        uint status = 0,
        uint latitude = 0,
        uint longitude = 0)
    {
        AddUInt32(body, status);
        AddUInt32(body, latitude);
        AddUInt32(body, longitude);
    }

    private static void AddObservedP1457DtcRecord(List<byte> body)
    {
        // Exact 16-byte record retained from the OPEN_SOURCE_FIXTURE only.
        body.AddRange(new byte[]
        {
            0x00, 0x00, 0x14, 0x57,
            0x00, 0x00, 0x00, 0x02,
            0x00, 0x06,
            0x50, 0x31, 0x34, 0x35, 0x37, 0x00
        });
    }

    private static byte[] BuildV111ParameterQueryResponseBody(
        params (ushort Id, byte[] Value)[] parameters)
    {
        if (parameters.Length > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(parameters));

        var body = new List<byte>();
        AddUInt16(body, 0x1234); // response sequence
        body.Add((byte)parameters.Length);

        foreach ((ushort id, byte[] value) in parameters)
        {
            if (value.Length > byte.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(parameters));

            AddUInt16(body, id); // V1.1.1 vendor contract: WORD ID
            body.Add((byte)value.Length);
            body.AddRange(value);
        }

        return body.ToArray();
    }

    private static byte[] BuildLocationBody(params byte[][] extensions)
    {
        var body = new List<byte>();
        AddUInt32(body, 0); // alarm
        AddUInt32(body, 0x00000003); // ACC on + located
        AddUInt32(body, 40_765_432);
        AddUInt32(body, 29_987_654);
        AddUInt16(body, 10);
        AddUInt16(body, 523);
        AddUInt16(body, 142);
        body.AddRange(new byte[] { 0x26, 0x08, 0x25, 0x19, 0x40, 0x30 });

        foreach (byte[] extension in extensions)
            body.AddRange(extension);

        return body.ToArray();
    }

    private static byte[] BuildLocationExtension(byte id, byte[] payload)
    {
        if (payload.Length > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(payload));

        var extension = new List<byte> { id, (byte)payload.Length };
        extension.AddRange(payload);
        return extension.ToArray();
    }

    private static byte[] BuildVl502E8Payload(ushort technology)
    {
        // MANUAL_DERIVED from V1.1.1 Table 24. The contract states
        // 1 < N < 7, so N=2 is the smallest structurally valid station count.
        // packetLength = 5 + 7*2 = 19; complete E8 payload = 3 + 19 = 22 bytes.
        var payload = new List<byte>();
        AddUInt16(payload, technology);
        payload.Add(19);         // 5 + 7 * 2
        AddUInt16(payload, 460); // MCC
        AddUInt16(payload, 0);   // MNC
        payload.Add(2);          // base-station count

        AddUInt16(payload, 0x1234);
        AddUInt32(payload, 0x000ABCDE);
        payload.Add(0x40);

        AddUInt16(payload, 0x1235);
        AddUInt32(payload, 0x000ABCDF);
        payload.Add(0x41);
        return payload.ToArray();
    }

    private static byte[] BuildV111RegistrationBody(string terminalModel, string? vin)
    {
        var body = new List<byte>();
        AddUInt16(body, 0x0000); // Provincial ID
        AddUInt16(body, 0x0000); // City/County ID
        AddFixedAscii(body, "JIMI0", 5); // Manufacturer ID
        AddFixedAscii(body, terminalModel, 20); // Terminal Model
        AddFixedAscii(body, "0000007", 7); // Terminal ID
        body.Add(vin is null ? (byte)0x01 : (byte)0x00); // plate color 0 => VIN follows

        if (vin is not null)
            body.AddRange(Encoding.ASCII.GetBytes(vin));

        return body.ToArray();
    }

    private static void AddFixedAscii(List<byte> target, string value, int length)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(value);
        if (bytes.Length > length)
            throw new ArgumentOutOfRangeException(nameof(value));

        target.AddRange(bytes);
        target.AddRange(Enumerable.Repeat((byte)0x00, length - bytes.Length));
    }

    private static void AssertKnownMalformedFieldHasNoSemanticValue(
        ushort id,
        DeviceInfoModel result)
    {
        switch (id)
        {
            case 0x0102:
            case 0x0528:
            case 0x0546:
                Assert.Null(result.OBDData!.OdometerKm);
                break;
            case 0x0105:
            case 0x052C:
                Assert.Null(result.OBDData!.FuelUsedL);
                break;
            case 0x0127:
                Assert.Null(result.OBDData!.EngineHours);
                break;
            case 0x052B:
            case 0x0544:
                Assert.Null(result.OBDData!.FuelLevelPercent);
                break;
            case 0x052D:
                Assert.Null(result.OBDData!.CoolantTempC);
                break;
            case 0x0530:
            case 0x0703:
                Assert.Null(result.BatteryVoltage);
                Assert.Null(result.BatteryLevel);
                break;
            case 0x0704:
                Assert.Null(result.BatteryCurrent);
                break;
            case 0x0705:
                Assert.Null(result.BatteryLevel);
                break;
            case 0x0535:
                Assert.Null(result.OBDData!.SpeedKmh);
                break;
            case 0x0536:
                Assert.Null(result.OBDData!.RpmValue);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(id));
        }
    }

    private static byte[] UInt16Bytes(ushort value)
        => new[] { (byte)(value >> 8), (byte)(value & 0xFF) };

    private static byte[] UInt32Bytes(uint value)
        => new[]
        {
            (byte)(value >> 24),
            (byte)(value >> 16),
            (byte)(value >> 8),
            (byte)value
        };

    private static void AddUInt16(List<byte> target, ushort value)
        => target.AddRange(UInt16Bytes(value));

    private static void AddUInt32(List<byte> target, uint value)
        => target.AddRange(UInt32Bytes(value));
}
