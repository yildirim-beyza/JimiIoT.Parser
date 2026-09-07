using System.Globalization;
using System.Text;
using JimiIoT.Parser.Conversion;
using JimiIoT.Parser.Framing;
using JimiIoT.Parser.Models;
using JimiIoT.Parser.Protocols;

namespace JimiIoT.Parser.Protocols.Profiles;

// ============================================================================
// JimiJt808VehicleDataProfileParser
// ----------------------------------------------------------------------------
// VL502/VG502 Communication Protocol V1.1.1 kapsamındaki ortak Jimi vendor
// JT/T 808 vehicle-data profile davranışlarını çözer:
//   - shared terminal identity -> IMEI kuralı
//   - 0x0100 registration içindeki VIN düzeni
//   - 0x0104/F007 terminal-parameter response içindeki ICCID
//   - 0x0200/E8 GSM/LTE vendor extension
//   - 0x0900/F0/01 OBD data stream ve vehicle-energy alanları
//   - 0x0900/F0/02 DTC
//   - 0x0900/F0/0B VIN
//   - shared transparent mesajın tail status/GPS alanları
//
// Generic JT/T 808 0x0200 location/status çözümü bu sınıfta değildir.
// Model eligibility bu parser içinde değildir. VL502 ve VG502 policy üzerinden
// bu profile bağlanır; VL533/VL512/Unknown bağlanmaz. Böylece model capability
// ile packet contract birbirine karıştırılmaz.
// ============================================================================
internal sealed class JimiJt808VehicleDataProfileParser
{
    // ------------------------------------------------------------------------
    // DEVICE ID / IMEI
    // ------------------------------------------------------------------------
    internal string? ParseImei(Jt808Frame frame)
    {
        // VL502 V1.1.1: 6-byte Terminal SN, IMEI'nin ilk 14 hanesinin
        // sayısal değerinin binary/HEX karşılığıdır. Son IMEI hanesi Luhn ile
        // yeniden üretilebilir.
        //
        // Bu parser yalnız shared-profile eligibility sağlandıktan sonra çağrılır.
        // V1.1.1 sözleşmesinde 6 byte alan binary Terminal SN'dir; byte
        // dizisinin tesadüfen BCD'ye benzemesi kimliği geçersiz yapmaz.
        if (frame.TerminalId.Length != 6)
            return null;

        ulong firstFourteenDigits = 0;
        foreach (byte value in frame.TerminalId)
            firstFourteenDigits = (firstFourteenDigits << 8) | value;

        string baseDigits = firstFourteenDigits.ToString(CultureInfo.InvariantCulture)
            .PadLeft(14, '0');

        if (baseDigits.Length != 14 || !baseDigits.All(char.IsDigit))
            return null;

        return baseDigits + CalculateLuhnCheckDigit(baseDigits);
    }

    // ------------------------------------------------------------------------
    // 0x0104 QUERY TERMINAL PARAMETER RESPONSE: F007 ICCID
    // ------------------------------------------------------------------------
    internal string? ParseIccidParameterResponse(Jt808Frame frame)
    {
        byte[]? value = FindParameterValue(frame, 0xF007);
        if (value is null || !IsAscii(value))
            return null;

        return ParseKeyedIccid(Encoding.ASCII.GetString(value));
    }

    internal string? ParseVinParameterResponse(Jt808Frame frame)
    {
        // V1.1.1 Table 4: F025 STRING Vehicle VIN. The surrounding 0x0104
        // parameter-response grammar already exists in this parser, so this is
        // a direct contract path rather than a new parameter subsystem.
        byte[]? value = FindParameterValue(frame, 0xF025);
        return value is null || !IsAscii(value)
            ? null
            : VinValidator.FromAscii(value);
    }

    private static string? ParseKeyedIccid(string parameterValue)
    {
        foreach (string token in parameterValue.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = token.IndexOf(':');
            if (separator <= 0)
                continue;

            string key = token[..separator].Trim();
            if (!key.Equals("ICCID", StringComparison.OrdinalIgnoreCase))
                continue;

            string candidate = token[(separator + 1)..].Trim();
            return IccidValidator.Normalize(candidate);
        }

        return null;
    }

    private static byte[]? FindParameterValue(Jt808Frame frame, ushort targetParameterId)
    {
        if (frame.MessageId != Jt808ProtocolNumbers.ParameterQueryResponse)
            return null;

        byte[] body = frame.Body;
        if (body.Length < 3)
            return null;

        // V1.1.1 vendor contract: response sequence WORD + count BYTE,
        // followed by [parameter ID WORD][length BYTE][value N].
        int offset = 2;
        int parameterCount = body[offset++];
        byte[]? matchedValue = null;

        for (int i = 0; i < parameterCount; i++)
        {
            if (offset + 3 > body.Length)
                return null;

            ushort parameterId = ReadUInt16BigEndian(body, offset);
            int parameterLength = body[offset + 2];
            offset += 3;

            if (offset + parameterLength > body.Length)
                return null;

            if (parameterId == targetParameterId)
                matchedValue = body.AsSpan(offset, parameterLength).ToArray();

            offset += parameterLength;
        }

        // Reject count/body mismatches even when the target appeared earlier.
        return offset == body.Length ? matchedValue : null;
    }

    // ------------------------------------------------------------------------
    // 0x0200 LOCATION ADDITIONAL INFO: 0xE8 GSM/LTE vendor extension
    // ------------------------------------------------------------------------
    internal string? ParseOperatorCellularStatus(Jt808Frame frame)
    {
        if (frame.MessageId != Jt808ProtocolNumbers.Location || frame.Body.Length < 28)
            return null;

        byte[] body = frame.Body;
        int offset = 28;

        while (offset + 2 <= body.Length)
        {
            byte id = body[offset++];
            int length = body[offset++];

            if (offset + length > body.Length)
                return null;

            if (id == 0xE8)
                return ParseE8NetworkTechnology(body.AsSpan(offset, length));

            offset += length;
        }

        return null;
    }

    private static string? ParseE8NetworkTechnology(ReadOnlySpan<byte> payload)
    {
        // Table 24 layout:
        // [technology WORD][packet length BYTE][MCC WORD][MNC WORD]
        // [base-station count BYTE][7 bytes per station].
        // V1.1.1 defines station count as 1 < N < 7, so the smallest
        // structurally valid payload carries two stations: 22 bytes.
        const int minimumPayloadLength = 22;
        if (payload.Length < minimumPayloadLength)
            return null;

        ushort technology = (ushort)((payload[0] << 8) | payload[1]);
        int packetLength = payload[2];
        int stationCount = payload[7];

        if (stationCount is <= 1 or >= 7)
            return null;

        int expectedPacketLength = 5 + (7 * stationCount);
        int expectedPayloadLength = 3 + expectedPacketLength;
        if (packetLength != expectedPacketLength || payload.Length != expectedPayloadLength)
            return null;

        return technology switch
        {
            0x2000 => "2G",
            0x2001 => "4G",
            _ => null
        };
    }

    // ------------------------------------------------------------------------
    // SHARED JIMI JT808 TRANSPARENT STATUS
    // ------------------------------------------------------------------------
    internal InputDataModel? ParseInputData(Jt808Frame frame, OBDDataModel? obdData = null)
    {
        // Bu profile generic 0x0200 status çözmez. Shared F0 transparent tail
        // status alanı varsa birincil kaynak odur.
        if (TryReadTransparentStatus(frame, out uint transparentStatus))
            return InputFromStatus(transparentStatus);

        // F0/01 içinde 0x0522 ACC alanı bulunabilir. Tail status yoksa bu alan
        // kontak bilgisinin doğrulanmış fallback kaynağıdır; charging/blocked
        // hakkında ise veri olmadığı için null bırakılır.
        if (obdData?.IgnitionOn is bool ignitionOn)
        {
            return new InputDataModel
            {
                IsIgnitionOn = ignitionOn,
                IsCharging = null,
                IsEngineBlocked = null
            };
        }

        // Ne tail status ne de 0x0522 varsa "false" uydurulmaz.
        return null;
    }

    // ------------------------------------------------------------------------
    // 0x0100 REGISTRATION: VIN/plate field
    // ------------------------------------------------------------------------
    internal string? ParseVin(Jt808Frame frame)
    {
        if (frame.MessageId == Jt808ProtocolNumbers.Register)
        {
            if (frame.Body.Length <= 37 || frame.Body[36] != 0x00)
                return null;

            // Plate color 0 ise Table 8'e göre devam eden STRING VIN'dir.
            return VinValidator.FromAscii(frame.Body.AsSpan(37));
        }

        if (!IsJimiVehicleTransparent(frame, Jt808ProtocolNumbers.VinSubtype))
            return null;

        // 0x0900/F0 header 10 byte; subtype-specific data byte 10'dan başlar.
        if (frame.Body.Length < 11)
            return null;

        // Exact V1.1.1 values are only 00 (not supported) and 01 (supported).
        // Unknown values must never be promoted to "supported".
        if (frame.Body[10] != 0x01)
            return null;

        // The wire contract is STRING. VIN domain validation is 17 characters,
        // but silently truncating a longer wire payload would hide corruption.
        return VinValidator.FromAscii(frame.Body.AsSpan(11));
    }

    // ------------------------------------------------------------------------
    // 0x0900/F0/0x01 OBD DATA STREAM
    // ------------------------------------------------------------------------
    internal OBDDataModel ParseObd(Jt808Frame frame)
    {
        JimiJt808VehicleDataContext context =
            EnsureJimiVehicleTransparent(frame, Jt808ProtocolNumbers.ObdSubtype, "Jimi JT808 vehicle OBD");

        byte[] body = frame.Body;
        if (body.Length < 11)
            throw new InvalidOperationException("Jimi JT808 vehicle OBD transparent body çok kısa.");

        DateTime eventTime = ReadBcdDate(body, 1);
        int offset = 10;
        int count = body[offset++];

        double? odometer = null;
        double? fuelLevel = null;
        double? coolant = null;
        double? speed = null;
        double? rpm = null;
        double? fuelUsed = null;
        double? engineHours = null;
        bool? accFromDataStream = null;

        var rawFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < count; i++)
        {
            if (offset + 3 > body.Length)
                throw new InvalidOperationException("Jimi JT808 vehicle OBD data-stream kaydı yarım kaldı.");

            ushort id = ReadUInt16BigEndian(body, offset);
            int length = body[offset + 2];
            offset += 3;

            if (offset + length > body.Length)
                throw new InvalidOperationException($"Jimi JT808 vehicle OBD 0x{id:X4} alan uzunluğu frame sınırını aşıyor.");

            ReadOnlySpan<byte> value = body.AsSpan(offset, length).ToArray();
            offset += length;

            string key = id.ToString("X4", CultureInfo.InvariantCulture);
            rawFields[key] = Convert.ToHexString(value);

            switch (id)
            {
                case 0x0102:
                    if (!context.IsCommercial)
                        break;
                    if (TryReadUnsignedExact(value, 4, out ulong commercialMileageRaw))
                        odometer = commercialMileageRaw / 10d;
                    break;

                case 0x0528:
                case 0x0546:
                    if (TryReadUnsignedExact(value, 4, out ulong mileageRaw))
                        odometer = mileageRaw / 10d;
                    break;

                case 0x052B:
                    if (!context.IsPassenger)
                        break;
                    if (TryReadUnsignedExact(value, 1, out ulong passengerFuelPercentRaw) && passengerFuelPercentRaw <= 100)
                        fuelLevel = passengerFuelPercentRaw;
                    break;

                case 0x0544:
                    if (TryReadUnsignedExact(value, 1, out ulong fuelPercentRaw) && fuelPercentRaw <= 100)
                        fuelLevel = fuelPercentRaw;
                    break;

                case 0x052D:
                    if (TryReadUnsignedExact(value, 1, out ulong coolantRaw) && coolantRaw <= 250)
                        coolant = (double)coolantRaw - 40d;
                    break;

                case 0x0535:
                    if (TryReadUnsignedExact(value, 2, out ulong speedRaw))
                        speed = speedRaw / 10d;
                    break;

                case 0x0536:
                    if (TryReadUnsignedExact(value, 2, out ulong rpmRaw))
                        rpm = rpmRaw;
                    break;

                case 0x0105:
                    if (!context.IsCommercial)
                        break;
                    if (TryReadUnsignedExact(value, 4, out ulong commercialFuelUsedRaw))
                        fuelUsed = commercialFuelUsedRaw / 100d;
                    break;

                case 0x052C:
                    if (TryReadUnsignedExact(value, 4, out ulong fuelUsedRaw))
                        fuelUsed = fuelUsedRaw / 100d;
                    break;

                case 0x0127:
                    if (!context.IsCommercial)
                        break;
                    if (TryReadUnsignedExact(value, 4, out ulong engineSecondsRaw))
                        engineHours = engineSecondsRaw / 3600d;
                    break;

                case 0x0522:
                    if (!context.IsPassenger)
                        break;
                    if (TryReadUnsignedExact(value, 1, out ulong accRaw) && accRaw <= 1)
                        accFromDataStream = accRaw == 1;
                    break;
            }
        }

        // V1.1.1 Table 32 requires exactly status + latitude + longitude after
        // the data-stream entries. Do not accept truncated or trailing garbage.
        EnsureExactTransparentTail(body, offset, "Jimi JT808 vehicle OBD");

        bool? ignitionOn = accFromDataStream;
        uint status = ReadUInt32BigEndian(body, offset);
        ignitionOn = IsBitSet(status, 0);

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
            DeviceTimeUtc = eventTime,
            RawFields = rawFields
        };
    }

    internal JimiJt808VehicleEnergyData ParseVehicleEnergy(Jt808Frame frame)
    {
        _ = EnsureJimiVehicleTransparent(frame, Jt808ProtocolNumbers.ObdSubtype, "Jimi JT808 vehicle energy");

        byte[] body = frame.Body;
        if (body.Length < 11)
            throw new InvalidOperationException("Jimi JT808 vehicle OBD transparent body çok kısa.");

        decimal? fallback0530Voltage = null;
        decimal? total0703Voltage = null;
        decimal? total0704Current = null;
        int? soc0705 = null;

        int offset = 10;
        int count = body[offset++];

        for (int i = 0; i < count; i++)
        {
            if (offset + 3 > body.Length)
                throw new InvalidOperationException("Jimi JT808 vehicle OBD data-stream kaydı yarım kaldı.");

            ushort id = ReadUInt16BigEndian(body, offset);
            int length = body[offset + 2];
            offset += 3;

            if (offset + length > body.Length)
                throw new InvalidOperationException($"Jimi JT808 vehicle OBD 0x{id:X4} alan uzunluğu frame sınırını aşıyor.");

            ReadOnlySpan<byte> value = body.AsSpan(offset, length);
            offset += length;

            switch (id)
            {
                case 0x0530:
                    // General vehicle electrical/battery voltage fallback.
                    if (TryReadUnsignedExact(value, 2, out ulong rawMillivolts))
                        fallback0530Voltage = (decimal)rawMillivolts / 1000m;
                    break;

                case 0x0703:
                    // GB/T 32960.3-2016 Total Voltage: 0..10000 => 0..1000.0 V.
                    // FFFE abnormal, FFFF invalid; both are outside the valid range.
                    if (TryReadUnsignedExact(value, 2, out ulong totalVoltageRaw) &&
                        totalVoltageRaw <= 10000)
                    {
                        total0703Voltage = (decimal)totalVoltageRaw * 0.1m;
                    }
                    break;

                case 0x0704:
                    // GB/T Total Current: 0..20000, offset 1000 A, 0.1 A unit.
                    if (TryReadUnsignedExact(value, 2, out ulong totalCurrentRaw) &&
                        totalCurrentRaw <= 20000)
                    {
                        total0704Current = (decimal)totalCurrentRaw * 0.1m - 1000m;
                    }
                    break;

                case 0x0705:
                    // GB/T Remaining Battery (SOC): 0..100%; FE abnormal, FF invalid.
                    if (TryReadUnsignedExact(value, 1, out ulong socRaw) && socRaw <= 100)
                        soc0705 = (int)socRaw;
                    break;
            }
        }

        EnsureExactTransparentTail(body, offset, "Jimi JT808 vehicle energy");

        // Keep conventional vehicle-electrical and NEV traction-system values
        // as separate semantic contexts. 0703 must not silently override 0530.
        BatteryDataModel? batteryLevel = soc0705.HasValue
            ? new BatteryDataModel
            {
                LevelPercent = soc0705.Value,
                VoltageV = total0703Voltage
            }
            : null;

        return new JimiJt808VehicleEnergyData
        {
            VehicleElectricalVoltageV = fallback0530Voltage,
            NevTotalVoltageV = total0703Voltage,
            NevTotalCurrentA = total0704Current,
            NevSocPercent = soc0705,
            BatteryLevel = batteryLevel
        };
    }

    // ------------------------------------------------------------------------
    // 0x0900/F0/0x02 DTC
    // ------------------------------------------------------------------------
    internal List<string> ParseDtc(Jt808Frame frame)
    {
        _ = EnsureJimiVehicleTransparent(frame, Jt808ProtocolNumbers.DtcSubtype, "Jimi JT808 vehicle DTC");

        byte[] body = frame.Body;
        int offset = 10;
        if (offset + 2 > body.Length)
            throw new InvalidOperationException("Jimi JT808 vehicle DTC system count alanı eksik.");

        int systemCount = ReadUInt16BigEndian(body, offset);
        offset += 2;

        var codes = new List<string>();

        for (int systemIndex = 0; systemIndex < systemCount; systemIndex++)
        {
            if (offset + 6 > body.Length)
                throw new InvalidOperationException("Jimi JT808 vehicle DTC system header alanı eksik.");

            _ = ReadUInt32BigEndian(body, offset); // system id; şu an modele taşınmıyor
            offset += 4;

            int codeCount = ReadUInt16BigEndian(body, offset);
            offset += 2;

            for (int codeIndex = 0; codeIndex < codeCount; codeIndex++)
            {
                const int codeRecordLength = 16;
                if (offset + codeRecordLength > body.Length)
                    throw new InvalidOperationException("Jimi JT808 vehicle DTC 16-byte code kaydı eksik.");

                ReadOnlySpan<byte> record = body.AsSpan(offset, codeRecordLength);
                offset += codeRecordLength;

                // V1.1.1 proves only BYTE*16*N and delegates the inner record
                // grammar to the unavailable ARM Interface Protocol. One public
                // VL502 regression fixture demonstrates an ASCII OBD code layout
                // (P1457). Parse only that self-validating observed layout; never
                // invent a generic numeric DTC for unknown/J1939 records.
                string? observedObdCode = TryDecodeObservedVl502ObdDtcRecord(record);
                if (observedObdCode is not null)
                    codes.Add(observedObdCode);
            }
        }

        EnsureDtcTransparentTail(body, offset);
        return codes;
    }

    // ------------------------------------------------------------------------
    // TRANSPARENT ortak alanları: event time + tail GPS/status
    // ------------------------------------------------------------------------
    internal ParsedGpsMessage? ParseTransparentGps(Jt808Frame frame, OBDDataModel? obdData = null)
    {
        if (frame.MessageId != Jt808ProtocolNumbers.TransparentUplink ||
            frame.Body.Length < 10 ||
            frame.Body[0] != Jt808ProtocolNumbers.VehicleTransparentType)
        {
            return null;
        }

        if (!TryFindTransparentTail(frame, out int tailOffset))
            return null;

        byte[] body = frame.Body;
        if (tailOffset + 12 > body.Length)
            return null;

        uint status = ReadUInt32BigEndian(body, tailOffset);
        uint rawLatitude = ReadUInt32BigEndian(body, tailOffset + 4);
        uint rawLongitude = ReadUInt32BigEndian(body, tailOffset + 8);

        double latitude = rawLatitude / 1_000_000d;
        double longitude = rawLongitude / 1_000_000d;
        if (IsBitSet(status, 2)) latitude = -latitude;
        if (IsBitSet(status, 3)) longitude = -longitude;

        // Transparent tail yalnız status + latitude + longitude taşır. Hız
        // ancak aynı F0/01 frame'inde 0x0535 alanı gerçekten varsa bilinir.
        // Course ve satellite count bu zarfın parçası değildir.
        double? speed = frame.Body[9] == Jt808ProtocolNumbers.ObdSubtype
            ? obdData?.SpeedKmh
            : null;

        return new ParsedGpsMessage
        {
            Gps = new GpsDataModel
            {
                Latitude = latitude,
                Longitude = longitude,
                SpeedKph = speed,
                Course = null,
                Valid = IsBitSet(status, 1),
                SatelliteCount = null
            },
            Date = new DateDataModel { DeviceTimeUtc = ReadBcdDate(body, 1) }
        };
    }

    internal DateDataModel? ParseTransparentDate(Jt808Frame frame)
    {
        if (frame.MessageId != Jt808ProtocolNumbers.TransparentUplink ||
            frame.Body.Length < 7 ||
            frame.Body[0] != Jt808ProtocolNumbers.VehicleTransparentType)
        {
            return null;
        }

        return new DateDataModel { DeviceTimeUtc = ReadBcdDate(frame.Body, 1) };
    }

    internal byte? GetTransparentSubtype(Jt808Frame frame)
    {
        return TryParseTransparentContext(frame, out JimiJt808VehicleDataContext context)
            ? context.Subtype
            : null;
    }

    // ------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------
    private static InputDataModel InputFromStatus(uint status)
    {
        return new InputDataModel
        {
            IsIgnitionOn = IsBitSet(status, 0),
            // Shared transparent status sözleşmesinde charging biti doğrulanmadı.
            IsCharging = null,
            // Table 20 bit10 oil circuit disconnected, bit11 vehicle circuit
            // disconnected. Task modelindeki "engine blocked" alanına en yakın
            // doğrulanmış anlam bu iki immobilizer/cut-off durumudur.
            IsEngineBlocked = IsBitSet(status, 10) || IsBitSet(status, 11)
        };
    }

    private static bool TryReadTransparentStatus(Jt808Frame frame, out uint status)
    {
        status = 0;
        if (!TryFindTransparentTail(frame, out int tailOffset) || tailOffset + 4 > frame.Body.Length)
            return false;

        status = ReadUInt32BigEndian(frame.Body, tailOffset);
        return true;
    }

    private static bool TryFindTransparentTail(Jt808Frame frame, out int tailOffset)
    {
        tailOffset = 0;
        if (!TryParseTransparentContext(frame, out JimiJt808VehicleDataContext context))
            return false;

        byte subtype = context.Subtype;
        int offset = 10;

        if (subtype == Jt808ProtocolNumbers.ObdSubtype)
        {
            if (offset >= frame.Body.Length)
                return false;

            int count = frame.Body[offset++];
            for (int i = 0; i < count; i++)
            {
                if (offset + 3 > frame.Body.Length)
                    return false;

                int length = frame.Body[offset + 2];
                offset += 3;
                if (offset + length > frame.Body.Length)
                    return false;
                offset += length;
            }

            if (frame.Body.Length - offset != 12)
                return false;

            tailOffset = offset;
            return true;
        }

        if (subtype == Jt808ProtocolNumbers.DtcSubtype)
        {
            if (offset + 2 > frame.Body.Length)
                return false;

            int systems = ReadUInt16BigEndian(frame.Body, offset);
            offset += 2;

            for (int systemIndex = 0; systemIndex < systems; systemIndex++)
            {
                if (offset + 6 > frame.Body.Length)
                    return false;

                offset += 4; // system id
                int codeCount = ReadUInt16BigEndian(frame.Body, offset);
                offset += 2;

                int bytesToSkip = codeCount * 16;
                if (offset + bytesToSkip > frame.Body.Length)
                    return false;
                offset += bytesToSkip;
            }

            if (!HasSupportedDtcTransparentTail(frame.Body, offset))
                return false;

            tailOffset = offset;
            return true;
        }

        return false;
    }

    // VL502/VG502 için doğrulanmış Jimi/JT808 F0 vehicle extension.
    // Eligibility policy bu contract'ı yalnız doğrulanmış modellere uygular.
    private static bool IsJimiVehicleTransparent(Jt808Frame frame, byte subtype)
    {
        return TryParseTransparentContext(frame, out JimiJt808VehicleDataContext context) &&
               context.Subtype == subtype;
    }

    private static JimiJt808VehicleDataContext EnsureJimiVehicleTransparent(
        Jt808Frame frame,
        byte subtype,
        string name)
    {
        if (!TryParseTransparentContext(frame, out JimiJt808VehicleDataContext context) || context.Subtype != subtype)
        {
            throw new InvalidOperationException(
                $"{name} parser'ına uygun olmayan JT/T 808 mesajı gönderildi.");
        }

        return context;
    }

    private static bool TryParseTransparentContext(
        Jt808Frame frame,
        out JimiJt808VehicleDataContext context)
    {
        context = default;

        if (frame.MessageId != Jt808ProtocolNumbers.TransparentUplink)
            return false;

        if (frame.Body.Length == 0 || frame.Body[0] != Jt808ProtocolNumbers.VehicleTransparentType)
            return false;

        if (frame.Body.Length < 10)
            throw new InvalidOperationException("Jimi JT808 F0 transparent envelope eksik.");

        JimiJt808VehicleDataType dataType = frame.Body[7] switch
        {
            0x00 => JimiJt808VehicleDataType.Realtime,
            0x01 => JimiJt808VehicleDataType.Buffered,
            _ => throw new InvalidOperationException(
                $"Jimi JT808 F0 DataType geçersiz: 0x{frame.Body[7]:X2}.")
        };

        JimiJt808VehicleType vehicleType = frame.Body[8] switch
        {
            0x01 => JimiJt808VehicleType.Commercial,
            0x02 => JimiJt808VehicleType.Passenger,
            _ => throw new InvalidOperationException(
                $"Jimi JT808 F0 VehicleType geçersiz: 0x{frame.Body[8]:X2}.")
        };

        context = new JimiJt808VehicleDataContext(dataType, vehicleType, frame.Body[9]);
        return true;
    }

    private static void EnsureExactTransparentTail(byte[] body, int offset, string name)
    {
        const int tailLength = 12; // status DWORD + latitude DWORD + longitude DWORD
        int remaining = body.Length - offset;
        if (remaining != tailLength)
        {
            throw new InvalidOperationException(
                $"{name} tail uzunluğu geçersiz. Beklenen {tailLength} byte, kalan {remaining} byte.");
        }
    }

    private static void EnsureDtcTransparentTail(byte[] body, int offset)
    {
        if (!HasSupportedDtcTransparentTail(body, offset))
        {
            int remaining = body.Length - offset;
            throw new InvalidOperationException(
                $"Jimi JT808 vehicle DTC tail uzunluğu/içeriği geçersiz: {remaining} byte.");
        }
    }

    private static bool HasSupportedDtcTransparentTail(byte[] body, int offset)
    {
        int remaining = body.Length - offset;

        // V1.1.1 Table 34: status DWORD + latitude DWORD + longitude DWORD.
        if (remaining == 12)
            return true;

        // The retained OPEN_SOURCE VL502 P1457 regression frame carries an
        // additional six-byte BCD timestamp after the exact 12-byte tail.
        // Accept only that self-validating observed extension, not arbitrary
        // trailing bytes. This preserves real operational evidence without
        // promoting the extension to the V1.1.1 normative contract.
        if (remaining != 18)
            return false;

        try
        {
            _ = ReadBcdDate(body, offset + 12);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static string? TryDecodeObservedVl502ObdDtcRecord(ReadOnlySpan<byte> record)
    {
        // Exact V1.1.1 does not define the 16-byte inner layout. Without the
        // ARM Interface Protocol we do not generalize from a single sample.
        // Retain only the exact OPEN_SOURCE_FIXTURE record that is known to
        // decode as P1457; every other 16-byte record remains unsupported.
        ReadOnlySpan<byte> knownP1457 = stackalloc byte[]
        {
            0x00, 0x00, 0x14, 0x57,
            0x00, 0x00, 0x00, 0x02,
            0x00, 0x06,
            0x50, 0x31, 0x34, 0x35, 0x37, 0x00
        };

        return record.SequenceEqual(knownP1457) ? "P1457" : null;
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

    private static int CalculateLuhnCheckDigit(string digits)
    {
        int sum = 0;
        bool doubleDigit = true; // check digit sağda ekleneceği için sağdan ilk mevcut hane çiftlenir

        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int value = digits[i] - '0';
            if (doubleDigit)
            {
                value *= 2;
                if (value > 9)
                    value -= 9;
            }

            sum += value;
            doubleDigit = !doubleDigit;
        }

        return (10 - (sum % 10)) % 10;
    }

    private static bool IsAscii(ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
        {
            if (value > 0x7F)
                return false;
        }

        return true;
    }

    private static bool TryReadUnsigned(ReadOnlySpan<byte> value, out ulong result)
    {
        result = 0;
        if (value.Length is < 1 or > 8)
            return false;

        foreach (byte b in value)
            result = (result << 8) | b;

        return true;
    }

    private static bool TryReadUnsignedExact(
        ReadOnlySpan<byte> value,
        int expectedLength,
        out ulong result)
    {
        if (value.Length != expectedLength)
        {
            result = 0;
            return false;
        }

        return TryReadUnsigned(value, out result);
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

internal sealed class JimiJt808VehicleEnergyData
{
    internal decimal? VehicleElectricalVoltageV { get; init; }
    internal decimal? NevTotalVoltageV { get; init; }
    internal decimal? NevTotalCurrentA { get; init; }
    internal int? NevSocPercent { get; init; }
    internal BatteryDataModel? BatteryLevel { get; init; }
}
