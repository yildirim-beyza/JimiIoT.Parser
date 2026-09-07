using System.Globalization;
using System.Text;
using JimiIoT.Parser.Conversion;
using JimiIoT.Parser.Fields;
using JimiIoT.Parser.Framing;
using JimiIoT.Parser.Models;

namespace JimiIoT.Parser.Protocols;

// ============================================================================
// JimiProtocolParser
// ----------------------------------------------------------------------------
// Framing katmanından gelen DeviceFrame'in INFORMATION CONTENT kısmını
// yorumlayan teknik parser'dır. Bu sınıf dış API değildir; INTERNAL tutulur.
// Dışarıdaki görev sözleşmesi JimiDeviceDataParser içindeki Get... metodlarıdır.
//
// Kaynak disiplini:
// - Frame/login/status/GPS/OBD/Info alanları açık kaynak referans uygulama
//   davranışından ve rehberdeki kaynak matrisi üzerinden türetilmiştir.
// - Generic 0x8C key/value mapping referans decoder olarak korunur; ana
//   kapsamdaki VL512 için exact direct-server field map doğrulanana kadar
//   bu mapping model desteği olarak otomatik etkinleştirilmez.
//
// Rehber karşılığı: JIMI direct-parser alan eşlemeleri, null politikası ve test kanıtı.
// ============================================================================
internal class JimiProtocolParser
{
    // ------------------------------------------------------------------------
    // LOGIN / IMEI
    // ------------------------------------------------------------------------
    internal string ParseImei(DeviceFrame frame)
    {
        EnsureProtocol(frame, JimiProtocolNumbers.Login, "IMEI/login");

        if (frame.InformationContent.Length < 8)
            throw new InvalidOperationException("Login paketi en az 8 byte BCD terminal ID içermelidir.");

        return BcdConverter.ToImei15(frame.InformationContent.AsSpan(0, 8));
    }

    // ------------------------------------------------------------------------
    // GPS + DATE
    // ------------------------------------------------------------------------
    internal ParsedGpsMessage ParseGps(DeviceFrame frame)
    {
        if (!JimiProtocolNumbers.IsGps(frame.ProtocolNumber))
            throw new InvalidOperationException($"0x{frame.ProtocolNumber:X2} GPS mesajı olarak desteklenmiyor.");

        byte[] info = frame.InformationContent;

        // YY MM DD HH MM SS + sat + lat4 + lon4 + speed + flags2 = 18 byte.
        if (info.Length < 18)
            throw new InvalidOperationException("GPS payload'u en az 18 byte olmalıdır.");

        int offset = 0;

        int year = 2000 + info[offset++];
        int month = info[offset++];
        int day = info[offset++];
        int hour = info[offset++];
        int minute = info[offset++];
        int second = info[offset++];

        var deviceTime = new DateTime(
            year, month, day, hour, minute, second, DateTimeKind.Unspecified);

        int satellites = info[offset++] & 0x0F;

        uint latitudeRaw = ReadUInt32BigEndian(info, offset);
        offset += 4;

        uint longitudeRaw = ReadUInt32BigEndian(info, offset);
        offset += 4;

        double latitude = ScaleConverter.JimiCoordinate(latitudeRaw);
        double longitude = ScaleConverter.JimiCoordinate(longitudeRaw);

        double speedKph = info[offset++];

        int flags = (info[offset] << 8) | info[offset + 1];

        int course = flags & 0x03FF;                 // düşük 10 bit
        bool isNorth = ((flags >> 10) & 1) != 0;    // bit 10
        bool isWest = ((flags >> 11) & 1) != 0;     // bit 11
        bool valid = ((flags >> 12) & 1) != 0;      // bit 12

        if (!isNorth)
            latitude = -latitude;

        if (isWest)
            longitude = -longitude;

        return new ParsedGpsMessage
        {
            Gps = new GpsDataModel
            {
                Latitude = latitude,
                Longitude = longitude,
                SpeedKph = speedKph,
                Course = course,
                Valid = valid,
                SatelliteCount = satellites
            },
            Date = new DateDataModel
            {
                DeviceTimeUtc = deviceTime
            }
        };
    }

    // ------------------------------------------------------------------------
    // STATUS / INPUT
    // ------------------------------------------------------------------------
    internal InputDataModel ParseStatus(DeviceFrame frame)
    {
        if (frame.ProtocolNumber != JimiProtocolNumbers.Status &&
            frame.ProtocolNumber != JimiProtocolNumbers.Heartbeat)
        {
            throw new InvalidOperationException(
                $"Status byte bu parser'da 0x13/0x23 için ele alınıyor. Gelen: 0x{frame.ProtocolNumber:X2}");
        }

        if (frame.InformationContent.Length < 1)
            throw new InvalidOperationException("Status payload'u en az 1 byte olmalıdır.");

        byte statusByte = frame.InformationContent[0];

        return new InputDataModel
        {
            IsIgnitionOn = StatusByteExtractor.IsIgnitionOn(statusByte),
            IsCharging = StatusByteExtractor.IsCharging(statusByte),
            IsEngineBlocked = StatusByteExtractor.IsEngineBlocked(statusByte)
        };
    }

    // ------------------------------------------------------------------------
    // OBD / 0x8C
    // ------------------------------------------------------------------------
    internal OBDDataModel ParseObd(DeviceFrame frame)
    {
        EnsureProtocol(frame, JimiProtocolNumbers.Obd, "OBD");

        byte[] info = frame.InformationContent;

        // 6 byte tarih + 1 byte ignition olmadan OBD gövdesine geçemeyiz.
        if (info.Length < 7)
            throw new InvalidOperationException("OBD payload'u en az 7 byte olmalıdır.");

        int offset = 0;

        int year = 2000 + info[offset++];
        int month = info[offset++];
        int day = info[offset++];
        int hour = info[offset++];
        int minute = info[offset++];
        int second = info[offset++];

        var deviceTime = new DateTime(
            year, month, day, hour, minute, second, DateTimeKind.Unspecified);

        bool ignitionOn = info[offset++] > 0;

        string csv = Encoding.ASCII.GetString(info, offset, info.Length - offset)
            .Trim('\0', '\r', '\n', ' ');

        var rawFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        double? odometer = null;
        double? fuelLevel = null;
        double? coolant = null;
        double? speed = null;
        double? rpm = null;
        double? fuelUsed = null;
        double? engineHours = null;

        foreach (string pair in csv.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            int equalsIndex = pair.IndexOf('=');
            if (equalsIndex < 2 || equalsIndex == pair.Length - 1)
                continue;

            string key = pair[..2].ToUpperInvariant();
            string value = pair[(equalsIndex + 1)..].Trim();

            // VIN gibi sayısal olmayan alanlar da kaybolmasın diye önce RawFields'a yazılır.
            rawFields[key] = value;

            if (!int.TryParse(
                    value,
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out int rawNumber))
            {
                continue;
            }

            double scaledValue = ScaleConverter.ObdHundredths(rawNumber);

            switch (key)
            {
                case "28": odometer = scaledValue; break;
                case "2B": fuelLevel = scaledValue; break;
                case "2D": coolant = scaledValue; break;
                case "35": speed = scaledValue; break;
                case "36": rpm = scaledValue; break;
                case "47": fuelUsed = scaledValue; break;
                case "49": engineHours = scaledValue; break;
            }
        }

        return new OBDDataModel
        {
            OdometerKm = odometer,
            FuelLevelPercent = fuelLevel,
            CoolantTempC = coolant,
            SpeedKmh = speed,
            RpmValue = rpm,
            FuelUsedL = fuelUsed,
            EngineHours = engineHours,
            IgnitionOn = ignitionOn,
            DeviceTimeUtc = deviceTime,
            RawFields = rawFields
        };
    }

    internal string? ParseVin(OBDDataModel obdData)
    {
        if (obdData.RawFields.TryGetValue("4A", out string? vin))
            return VinValidator.Normalize(vin);

        return null;
    }

    // ------------------------------------------------------------------------
    // ICCID
    // ------------------------------------------------------------------------
    internal string? ParseIccid(DeviceFrame frame)
    {
        return frame.ProtocolNumber switch
        {
            JimiProtocolNumbers.StringMessage => ParseIccidFromStringMessage(frame),
            JimiProtocolNumbers.Info => ParseInfoMessage(frame).Iccid,
            _ => null
        };
    }

    private static string? ParseIccidFromStringMessage(DeviceFrame frame)
    {
        byte[] info = frame.InformationContent;
        if (info.Length < 5)
            return null;

        int commandLength = info[0];
        if (commandLength <= 4)
            return null;

        int dataLength = commandLength - 4;
        const int dataStart = 5;

        if (dataStart + dataLength > info.Length)
            return null;

        string data = Encoding.ASCII.GetString(info, dataStart, dataLength)
            .TrimEnd('\0', '\r', '\n', ' ');

        const string prefix = "<ICCID:";
        if (!data.StartsWith(prefix, StringComparison.Ordinal) ||
            data.Length <= prefix.Length ||
            data[^1] != '>')
        {
            return null;
        }

        string candidate = data[prefix.Length..^1];
        return IccidValidator.Normalize(candidate);
    }

    // ------------------------------------------------------------------------
    // 0x94 INFO: ICCID / 2G-4G / external voltage
    // ------------------------------------------------------------------------
    internal InfoMessageData ParseInfoMessage(DeviceFrame frame)
    {
        EnsureProtocol(frame, JimiProtocolNumbers.Info, "Info");

        byte[] info = frame.InformationContent;
        if (info.Length < 1)
            return new InfoMessageData();

        byte subType = info[0];
        ReadOnlySpan<byte> rest = info.AsSpan(1);

        if (subType == 0x0A && rest.Length >= 26)
        {
            // [IMEI 8B] [IMSI 8B] [ICCID 10B]
            string iccid = Convert.ToHexString(rest.Slice(16, 10)).TrimEnd('F');
            return new InfoMessageData { Iccid = IccidValidator.Normalize(iccid) };
        }

        if (subType == 0x0B && rest.Length == 1)
        {
            return new InfoMessageData
            {
                NetworkTechnology = rest[0] > 0 ? "4G" : "2G"
            };
        }

        if (subType == 0x0B && rest.Length == 2)
        {
            int rawVoltage = (rest[0] << 8) | rest[1];
            return new InfoMessageData
            {
                ExternalVoltageV = ScaleConverter.Hundredths(rawVoltage)
            };
        }

        return new InfoMessageData();
    }

    internal string? ParseNetworkTechnology(DeviceFrame frame)
        => ParseInfoMessage(frame).NetworkTechnology;

    // ------------------------------------------------------------------------
    // BATTERY / VOLTAGE
    // ------------------------------------------------------------------------
    internal BatteryDataModel ParseBattery(DeviceFrame frame)
    {
        return frame.ProtocolNumber switch
        {
            JimiProtocolNumbers.Heartbeat => ParseBatteryFromHeartbeat(frame),
            JimiProtocolNumbers.Status2 => ParseBatteryFromStatus2(frame),
            JimiProtocolNumbers.Info => ParseBatteryFromInfo(frame),
            _ => new BatteryDataModel()
        };
    }

    private static BatteryDataModel ParseBatteryFromHeartbeat(DeviceFrame frame)
    {
        byte[] info = frame.InformationContent;

        // [status 1B] [voltage 2B]
        if (info.Length < 3)
            return new BatteryDataModel();

        int rawVoltage = (info[1] << 8) | info[2];

        return new BatteryDataModel
        {
            VoltageV = ScaleConverter.Hundredths(rawVoltage)
        };
    }

    private static BatteryDataModel ParseBatteryFromStatus2(DeviceFrame frame)
    {
        byte[] info = frame.InformationContent;
        decimal? voltage = null;
        int? level = null;
        int offset = 0;

        // TLV: [moduleType 2B] [length 1B] [value NB]
        while (offset + 3 <= info.Length)
        {
            int moduleType = (info[offset] << 8) | info[offset + 1];
            int moduleLength = info[offset + 2];
            offset += 3;

            if (offset + moduleLength > info.Length)
                break;

            if (moduleType == 0x0018 && moduleLength >= 2)
            {
                int rawVoltage = (info[offset] << 8) | info[offset + 1];
                voltage = ScaleConverter.Hundredths(rawVoltage);
            }
            else if (moduleType == 0x006A && moduleLength >= 1)
            {
                level = info[offset];
            }

            offset += moduleLength;
        }

        return new BatteryDataModel
        {
            VoltageV = voltage,
            LevelPercent = level
        };
    }

    private BatteryDataModel ParseBatteryFromInfo(DeviceFrame frame)
    {
        InfoMessageData info = ParseInfoMessage(frame);
        return new BatteryDataModel { VoltageV = info.ExternalVoltageV };
    }

    // ------------------------------------------------------------------------
    // DTC
    // ------------------------------------------------------------------------
    internal List<string> ParseDtc(DeviceFrame frame)
    {
        if (frame.ProtocolNumber != JimiProtocolNumbers.Dtc &&
            frame.ProtocolNumber != JimiProtocolNumbers.Pid)
        {
            throw new InvalidOperationException(
                $"DTC parser'ına 0x{frame.ProtocolNumber:X2} mesajı gönderildi.");
        }

        // Referans uygulamada 0x65/0x66 mesaj sabitleri görülüyor fakat payload
        // içerik çözümü yok. Model-özel resmi protokol dokümanı olmadan offset
        // uydurmak yerine açık biçimde desteklenmediğini bildiriyoruz.
        throw new NotSupportedException(
            "0x65/0x66 DTC payload formatı kamuya açık kaynaklarla yeterince doğrulanamadı.");
    }

    // ------------------------------------------------------------------------
    // Küçük byte yardımcıları
    // ------------------------------------------------------------------------
    private static uint ReadUInt32BigEndian(byte[] source, int offset)
    {
        return ((uint)source[offset] << 24)
             | ((uint)source[offset + 1] << 16)
             | ((uint)source[offset + 2] << 8)
             | source[offset + 3];
    }

    private static void EnsureProtocol(DeviceFrame frame, byte expected, string operation)
    {
        if (frame.ProtocolNumber != expected)
        {
            throw new InvalidOperationException(
                $"{operation} için 0x{expected:X2} beklenirken 0x{frame.ProtocolNumber:X2} geldi.");
        }
    }
}
