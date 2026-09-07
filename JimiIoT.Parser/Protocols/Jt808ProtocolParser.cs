using JimiIoT.Parser.Framing;
using JimiIoT.Parser.Models;

namespace JimiIoT.Parser.Protocols;

// ============================================================================
// Jt808ProtocolParser
// ----------------------------------------------------------------------------
// Modelden bağımsız JT/T 808 mesaj semantiğini çözer.
//
// Bu sınıf yalnız generic/core davranışları taşır:
//   - standart 0x0200 location/status/time alanları
//   - doğrulanmış 0x0200 additional-info alanları
//
// VL502/VG502 shared 0x0100/F007/E8/F0 vehicle-data uzantıları burada bulunmaz;
// Protocols/Profiles/JimiJt808VehicleDataProfileParser içinde tutulur.
// Model eligibility ayrı policy'dedir; generic JT808 olmak tek başına bu
// vendor profile'ı kullanmak için yeterli değildir.
// ============================================================================
internal sealed class Jt808ProtocolParser
{
    // ------------------------------------------------------------------------
    // 0x0200 LOCATION
    // ------------------------------------------------------------------------
    internal ParsedGpsMessage ParseLocation(Jt808Frame frame)
    {
        EnsureMessage(frame, Jt808ProtocolNumbers.Location, "Location");

        byte[] body = frame.Body;
        if (body.Length < 28)
            throw new InvalidOperationException("JT/T 808 0x0200 body en az 28 byte olmalıdır.");

        uint status = ReadUInt32BigEndian(body, 4);
        uint rawLatitude = ReadUInt32BigEndian(body, 8);
        uint rawLongitude = ReadUInt32BigEndian(body, 12);
        ushort rawSpeed = ReadUInt16BigEndian(body, 18);
        ushort direction = ReadUInt16BigEndian(body, 20);
        DateTime time = ReadBcdDate(body, 22);

        double latitude = rawLatitude / 1_000_000d;
        double longitude = rawLongitude / 1_000_000d;

        // JT/T 808 status: bit2 south, bit3 west.
        if (IsBitSet(status, 2)) latitude = -latitude;
        if (IsBitSet(status, 3)) longitude = -longitude;

        int? satellites = ParseSatelliteCountFromLocationExtensions(body);

        return new ParsedGpsMessage
        {
            Gps = new GpsDataModel
            {
                Latitude = latitude,
                Longitude = longitude,
                SpeedKph = rawSpeed / 10d,
                Course = direction,
                Valid = IsBitSet(status, 1),
                SatelliteCount = satellites
            },
            Date = new DateDataModel { DeviceTimeUtc = time }
        };
    }

    internal InputDataModel ParseInputData(Jt808Frame frame)
    {
        EnsureMessage(frame, Jt808ProtocolNumbers.Location, "Location status");

        if (frame.Body.Length < 8)
            throw new InvalidOperationException("JT/T 808 location status alanı eksik.");

        uint status = ReadUInt32BigEndian(frame.Body, 4);
        return InputFromStatus(status);
    }

    internal BatteryDataModel ParseBattery(Jt808Frame frame)
    {
        if (frame.MessageId != Jt808ProtocolNumbers.Location)
            return new BatteryDataModel();

        decimal? voltage = ParseVoltageFromLocationExtensions(frame.Body);
        return new BatteryDataModel { VoltageV = voltage };
    }

    // ------------------------------------------------------------------------
    // Generic 0x0200 helpers
    // ------------------------------------------------------------------------
    private static InputDataModel InputFromStatus(uint status)
    {
        return new InputDataModel
        {
            IsIgnitionOn = IsBitSet(status, 0),
            // Generic JT/T 808 0x0200 status içinde charging semantiği yoktur.
            IsCharging = null,
            // bit10/bit11 cut-off durumları görev modelindeki engine-blocked
            // alanına en yakın doğrulanmış generic status anlamıdır.
            IsEngineBlocked = IsBitSet(status, 10) || IsBitSet(status, 11)
        };
    }

    private static int? ParseSatelliteCountFromLocationExtensions(byte[] body)
    {
        int offset = 28;
        while (offset + 2 <= body.Length)
        {
            byte id = body[offset++];
            int length = body[offset++];
            if (offset + length > body.Length)
                break;

            if (id == 0x31 && length == 1)
                return body[offset];

            offset += length;
        }

        return null;
    }

    private static decimal? ParseVoltageFromLocationExtensions(byte[] body)
    {
        int offset = 28;
        while (offset + 2 <= body.Length)
        {
            byte id = body[offset++];
            int length = body[offset++];
            if (offset + length > body.Length)
                break;

            if (id == 0x61 && length >= 2)
                return ReadUInt16BigEndian(body, offset) / 100m;

            if (id == 0x69 && length >= 2)
                return ReadUInt16BigEndian(body, offset) / 100m;

            offset += length;
        }

        return null;
    }

    private static void EnsureMessage(Jt808Frame frame, ushort expected, string name)
    {
        if (frame.MessageId != expected)
        {
            throw new InvalidOperationException(
                $"{name} parser'ına 0x{frame.MessageId:X4} mesajı gönderildi.");
        }
    }

    private static DateTime ReadBcdDate(byte[] source, int offset)
    {
        if (offset + 6 > source.Length)
            throw new InvalidOperationException("BCD tarih alanı eksik.");

        int year = 2000 + BcdByteToInt(source[offset]);
        int month = BcdByteToInt(source[offset + 1]);
        int day = BcdByteToInt(source[offset + 2]);
        int hour = BcdByteToInt(source[offset + 3]);
        int minute = BcdByteToInt(source[offset + 4]);
        int second = BcdByteToInt(source[offset + 5]);

        return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
    }

    private static int BcdByteToInt(byte value)
    {
        int high = (value >> 4) & 0x0F;
        int low = value & 0x0F;
        if (high > 9 || low > 9)
            throw new InvalidOperationException($"Geçersiz BCD byte: 0x{value:X2}");

        return high * 10 + low;
    }

    private static ushort ReadUInt16BigEndian(byte[] source, int offset)
        => (ushort)((source[offset] << 8) | source[offset + 1]);

    private static uint ReadUInt32BigEndian(byte[] source, int offset)
    {
        return ((uint)source[offset] << 24)
             | ((uint)source[offset + 1] << 16)
             | ((uint)source[offset + 2] << 8)
             | source[offset + 3];
    }

    private static bool IsBitSet(uint value, int bit)
        => (value & (1u << bit)) != 0;
}
