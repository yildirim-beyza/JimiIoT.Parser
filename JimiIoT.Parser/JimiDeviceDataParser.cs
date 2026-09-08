using JimiIoT.Parser.Conversion;
using JimiIoT.Parser.Framing;
using JimiIoT.Parser.Models;
using JimiIoT.Parser.Protocols;
using JimiIoT.Parser.Protocols.Profiles;

namespace JimiIoT.Parser;

// ============================================================================
// JimiDeviceDataParser
// ----------------------------------------------------------------------------
// PROJENİN ANA SINIFI.
//
// Erişim yapısı şu şekildedir:
//   public  DeviceInfoModel GetDeviceInfo(string deviceData)
//   private InputDataModel GetInputData(...)
//   private OBDDataModel GetParsedObdData(...)
//   private string GetParsedVIN(...)
//   private string GetParsedCCID(...)
//   private string GetParsedOperatorCellularStatus(...)
//   private decimal GetParsedBatteryVoltage(...)
//   private decimal? GetParsedBatteryCurrent(...)
//   private int GetVehicleStatus(...)
//   private GpsDataModel GetParsedGpsData(...)
//   private DateDataModel GetParsedDateData(...)
//   private BatteryDataModel GetParsedBatteryLevel(...)
//   private List<string> GetParsedDTCFaultCodes(...)
//
// Mimari:
//   - ProtocolResolver       = gelen frame JIMI mi JT/T 808 mi?
//   - JimiFrameDecoder       = JIMI framing / CRC
//   - JimiProtocolParser     = JIMI byte-level alan çözümü
//   - Jt808FrameDecoder          = 0x7E / unescape / XOR / header
//   - Jt808ProtocolParser        = modelden bağımsız JT/T 808 0x0200 çekirdeği
//   - JimiJt808VehicleDataProfileParser = VL502/VG502 shared Jimi JT808 vehicle profile
//
// Ana görev sözleşmesi korunur. ProtocolResolver model seçmez; model bilgisi
// yalnız framing sonrasında vendor/body profile seçmek için kullanılır.
// ============================================================================
public class JimiDeviceDataParser
{
    private readonly ProtocolResolver _protocolResolver;
    private readonly JimiDeviceModel _deviceModel;

    // JIMI Protocol bağımlılıkları
    private readonly JimiFrameDecoder _frameDecoder;
    private readonly JimiProtocolParser _protocolParser;

    // JT/T 808 bağımlılıkları
    private readonly Jt808FrameDecoder _jt808FrameDecoder;
    private readonly Jt808ProtocolParser _jt808ProtocolParser;
    private readonly JimiJt808VehicleDataProfileParser _jimiJt808VehicleDataProfileParser;

    // Güvenli varsayılan: model bilinmiyorsa yalnız generic protokol alanları
    // çözülür; hiçbir model-özel OBD/CAN mapping'i otomatik uygulanmaz.
    public JimiDeviceDataParser()
        : this(JimiDeviceModel.Unknown)
    {
    }

    // Model bilgisi framing seçimi için değil, framing sonrasındaki model-özel
    // payload profile'ını seçmek için kullanılır.
    public JimiDeviceDataParser(JimiDeviceModel deviceModel)
        : this(
            deviceModel,
            new ProtocolResolver(),
            new JimiFrameDecoder(),
            new JimiProtocolParser(),
            new Jt808FrameDecoder(),
            new Jt808ProtocolParser(),
            new JimiJt808VehicleDataProfileParser())
    {
    }

    internal JimiDeviceDataParser(
        JimiDeviceModel deviceModel,
        ProtocolResolver protocolResolver,
        JimiFrameDecoder frameDecoder,
        JimiProtocolParser protocolParser,
        Jt808FrameDecoder jt808FrameDecoder,
        Jt808ProtocolParser jt808ProtocolParser,
        JimiJt808VehicleDataProfileParser jimiJt808VehicleDataProfileParser)
    {
        _deviceModel = deviceModel;
        _protocolResolver = protocolResolver ?? throw new ArgumentNullException(nameof(protocolResolver));
        _frameDecoder = frameDecoder ?? throw new ArgumentNullException(nameof(frameDecoder));
        _protocolParser = protocolParser ?? throw new ArgumentNullException(nameof(protocolParser));
        _jt808FrameDecoder = jt808FrameDecoder ?? throw new ArgumentNullException(nameof(jt808FrameDecoder));
        _jt808ProtocolParser = jt808ProtocolParser ?? throw new ArgumentNullException(nameof(jt808ProtocolParser));
        _jimiJt808VehicleDataProfileParser = jimiJt808VehicleDataProfileParser ?? throw new ArgumentNullException(nameof(jimiJt808VehicleDataProfileParser));
    }

    // ========================================================================
    // PUBLIC METOT
    // ========================================================================
    public DeviceInfoModel GetDeviceInfo(string deviceData)
    {
        byte[] bytes = HexPayloadReader.ToBytes(deviceData);

        // Resolver yalnızca doğru teknik kola yönlendirir; payload parse etmez.
        ProtocolType protocol = _protocolResolver.Resolve(bytes);

        return protocol switch
        {
            ProtocolType.Jimi => ParseJimiDeviceInfo(bytes),
            ProtocolType.Jt808 => ParseJt808DeviceInfo(bytes),
            _ => throw new FormatException("Desteklenmeyen JimiIoT protokolü.")
        };
    }

    // ========================================================================
    // JIMI PROTOCOL KOLU
    // ========================================================================
    private DeviceInfoModel ParseJimiDeviceInfo(byte[] bytes)
    {
        if (!_frameDecoder.TryDecode(bytes, out DeviceFrame frame, out int consumed))
            throw new FormatException("deviceData geçerli bir JIMI Protocol frame'i değil veya CRC doğrulanamadı.");

        if (consumed != bytes.Length)
            throw new FormatException("deviceData tek bir JIMI frame içermelidir; sonda ek byte bulundu.");

        var result = new DeviceInfoModel
        {
            ProtocolSerialNumber = frame.SerialNumber,

            // Genel JIMI tarafında proje kapsamında doğrulanmış
            // araç akımı kaynağı bulunmamaktadır.
            BatteryCurrent = null
        };

        switch (frame.ProtocolNumber)
        {
            case JimiProtocolNumbers.Login:
                {
                    result.ExtractedImei = _protocolParser.ParseImei(frame);
                    break;
                }

            case var protocolNumber when JimiProtocolNumbers.IsGps(protocolNumber):
                {
                    ParsedGpsMessage parsedGps = _protocolParser.ParseGps(frame);

                    result.GpsData = GetParsedGpsData(parsedGps);
                    result.DateData = GetParsedDateData(parsedGps);
                    break;
                }

            case JimiProtocolNumbers.Status:
                {
                    InputDataModel inputData =
                        GetInputData(_protocolParser.ParseStatus(frame));

                    result.InputData = inputData;

                    if (inputData.IsIgnitionOn.HasValue)
                        result.VehicleStatus =
                            GetVehicleStatus(inputData.IsIgnitionOn.Value);

                    break;
                }

            case JimiProtocolNumbers.Heartbeat:
                {
                    InputDataModel inputData =
                        GetInputData(_protocolParser.ParseStatus(frame));

                    result.InputData = inputData;

                    if (inputData.IsIgnitionOn.HasValue)
                        result.VehicleStatus =
                            GetVehicleStatus(inputData.IsIgnitionOn.Value);

                    // Genel JIMI heartbeat mesajındaki batarya/voltaj alanları
                    // cihaz tarafına aittir veya proje kapsamındaki aktif modeller için
                    // araç enerji verisi olduğu doğrulanmamıştır.
                    // İç decoder korunur; ancak bu değerler araç tarafını temsil eden
                    // BatteryVoltage veya BatteryLevel alanlarına aktarılmaz.
                    break;
                }

            case JimiProtocolNumbers.Status2:
                {
                    // 0x36 batarya/voltaj TLV alanları genel decoder içinde
                    // referans amacıyla korunmaktadır. Ancak mevcut model kümesi için
                    // bu alanların araç tarafı enerji verisini temsil ettiği
                    // doğrulanmamıştır. Bu nedenle public araç enerji alanlarına
                    // aktarılmaz.
                    break;
                }

            case JimiProtocolNumbers.Obd:
                {
                    // Genel JIMI/Concox 0x8C decoder korunur; ancak VL512 için
                    // bu anahtar/ölçek haritasının model-özel doğrudan sunucu
                    // sözleşmesi henüz doğrulanmış değildir.
                    //
                    // Cihazın native OBD yeteneğini, genel 0x8C alan haritasının
                    // kullanıldığına dair kanıt olarak kabul etmiyoruz.
                    //
                    // Model-özel VL512 alan haritası doğrulandığında burada
                    // ayrı bir VL512 profili üzerinden doğrulanmış OBD yolu
                    // etkinleştirilebilir.
                    break;
                }

            case JimiProtocolNumbers.StringMessage:
                {
                    string ccid =
                        GetParsedCCID(_protocolParser.ParseIccid(frame));

                    result.CCID =
                        string.IsNullOrEmpty(ccid) ? null : ccid;

                    break;
                }

            case JimiProtocolNumbers.Info:
                {
                    string ccid =
                        GetParsedCCID(_protocolParser.ParseIccid(frame));

                    result.CCID =
                        string.IsNullOrEmpty(ccid) ? null : ccid;

                    // Genel JIMI 0x94/0x0B şebeke teknolojisi çözümü,
                    // doğrulanmış model-özel bir sözleşme olmadan public çıktıya
                    // aktarılmaz.
                    //
                    // Benzer şekilde genel 0x94 voltaj/batarya içeriği de
                    // kesin model kanıtı olmadan araç enerji alanlarına bağlanmaz.
                    break;
                }

            case JimiProtocolNumbers.Dtc:
            case JimiProtocolNumbers.Pid:
                {
                    // 0x65/0x66 mesaj ID'leri genel JIMI kaynaklarında bilinmektedir;
                    // ancak proje kapsamındaki JIMI modelleri için payload sözleşmesi
                    // henüz model-özel bir kaynakla doğrulanmamıştır.
                    //
                    // Bu nedenle public sonuç seviyesinde "destek bilinmiyor"
                    // durumu null ile temsil edilir.
                    //
                    // İç ParseDtc metodu koruma amacıyla hâlâ
                    // NotSupportedException üretir; doğrulanmış bir model profili
                    // eklenmeden bu akıştan çağrılmaz.
                    result.DTCFaultCodes = null;
                    break;
                }

            default:
                break;
        }

        return result;
    }
    // ========================================================================
    // JT/T 808 KOLU
    // ----------------------------------------------------------------------------
    // Jt808ProtocolParser yalnız generic 0x0200 location/status çekirdeğini
    // çözer. VL502/VG502 V1.1.1 ortak F007/E8/F0 vehicle-data contract'ı
    // JimiJt808VehicleDataProfileParser içinde tutulur. Eligibility merkezi
    // policy ile yalnız VL502 ve VG502 için true'dur; VL533/VL512/Unknown false.
    // ========================================================================
    private DeviceInfoModel ParseJt808DeviceInfo(byte[] bytes)
    {
        if (!_jt808FrameDecoder.TryDecode(bytes, out Jt808Frame frame, out int consumed))
            throw new FormatException("deviceData geçerli bir JT/T 808 frame'i değil veya XOR checksum doğrulanamadı.");

        if (consumed != bytes.Length)
            throw new FormatException("deviceData tek bir JT/T 808 frame içermelidir; sonda ek byte bulundu.");

        // Body encryption flag'i framing seviyesinde okunabilir; 
        // ancak şifreli JT/T 808 body çözümü yapılmaz. Şifreli payload'u düz veri gibi
        // yorumlayıp sessizce yanlış alan üretmek yerine açıkça reddediyoruz.
        if (frame.EncryptionType != 0)
            throw new NotSupportedException("Şifreli JT/T 808 message body bu parser sürümünde desteklenmiyor.");

        // Decoder subpackage header'ını okuyabilir; fakat bu kütüphane TCP/stream
        // seviyesinde parçaları birleştirmez. Tek parça tamamlanmış body varmış gibi
        // parse etmek sessiz veri üretir, bu nedenle bilinçli olarak reddedilir.
        if (frame.IsSubpackaged)
            throw new NotSupportedException("Birleştirilmemiş JT/T 808 subpackage mesajları desteklenmiyor.");

        bool useJimiVehicleProfile = JimiJt808VehicleDataProfilePolicy.Supports(_deviceModel);

        // Shared VL502/VG502 V1.1.1 sözleşmesi JT/T 808-2013 6-byte Terminal SN başlığına
        // dayanır. Decoder generic 2019 zarfını okuyabilse de shared profile için
        // 10-byte terminal identity semantiği doğrulanmadığından burada reddedilir.
        if (useJimiVehicleProfile && frame.IsVersioned)
            throw new NotSupportedException("Jimi VL502/VG502 vehicle-data profile yalnız JT/T 808-2013 başlığıyla desteklenmektedir.");

        var result = new DeviceInfoModel
        {
            ProtocolSerialNumber = frame.SerialNumber,
            // Terminal ID → IMEI dönüşümü shared V1.1.1 profile kuralıdır.
            // Model bilinmiyorsa veya profile eligibility yoksa uygulanmaz.
            ExtractedImei = useJimiVehicleProfile
                ? _jimiJt808VehicleDataProfileParser.ParseImei(frame)
                : null,
            BatteryCurrent = null
        };

        switch (frame.MessageId)
        {
            case Jt808ProtocolNumbers.ParameterQueryResponse:
            {
                    // VL502/VG502 V1.1.1 kapsamında 0x0104 gelen parametre yanıtıdır.
                    // Shared profile eligibility her iki doğrulanmış model için etkindir.
                    // Giden 0x8106 sorgusunun üretilmesi üst entegrasyon katmanının
                    // sorumluluğundadır ve bu kütüphanede uygulanmaz.
                    if (useJimiVehicleProfile)
                    {
                        string ccid =
                            GetParsedCCID(
                                _jimiJt808VehicleDataProfileParser
                                    .ParseIccidParameterResponse(frame));

                        result.CCID =
                            string.IsNullOrEmpty(ccid) ? null : ccid;

                        string vin =
                            GetParsedVIN(
                                _jimiJt808VehicleDataProfileParser
                                    .ParseVinParameterResponse(frame));

                        result.VIN =
                            string.IsNullOrEmpty(vin) ? null : vin;
                    }
                    break;
            }

            case Jt808ProtocolNumbers.Register:
            {
                // VL502/VG502 V1.1.1 registration body artık ICCID taşımaz.
                // Body[9..28] Terminal Model alanıdır; CCID için yanlış bir
                // fallback üretilmez. Plate color 0 ise Body[37..] VIN'dir.
                if (useJimiVehicleProfile)
                {
                    string vin = GetParsedVIN(_jimiJt808VehicleDataProfileParser.ParseVin(frame));
                    result.VIN = string.IsNullOrEmpty(vin) ? null : vin;
                }

                break;
            }

            case Jt808ProtocolNumbers.Location:
            {
                    // Standart 0x0200 çekirdeği modelden bağımsız parser'da kalır.
                    ParsedGpsMessage parsedGps =
                        _jt808ProtocolParser.ParseLocation(frame);

                    InputDataModel input =
                        GetInputData(_jt808ProtocolParser.ParseInputData(frame));

                    result.GpsData = GetParsedGpsData(parsedGps);
                    result.DateData = GetParsedDateData(parsedGps);
                    result.InputData = input;

                    if (input.IsIgnitionOn.HasValue)
                        result.VehicleStatus =
                            GetVehicleStatus(input.IsIgnitionOn.Value);
                    
                    // 0xE8, VL502/VG502 V1.1.1 sözleşmesine ait üretici-özel bir uzantıdır;
                    // genel JT/T 808 standardının bir alanı değildir.
                    // Shared profile yalnız VL502/VG502 eligibility altında etkindir.
                    // 0x30 sinyal gücü alanı bilinçli olarak 2G/4G çıkarmak için kullanılmaz.
                    if (useJimiVehicleProfile)
                {
                    string operatorStatus = GetParsedOperatorCellularStatus(frame);
                    result.OperatorCellularStatus =
                    string.IsNullOrEmpty(operatorStatus) ? null : operatorStatus;
                }

                // Genel JT/T 808 0x61/0x69 voltaj yardımcıları yalnız iç referans
                // decoder'ları olarak korunur.
                // Bu alanların araç bataryası anlamına geldiği doğrulanmadığından
                // public araç enerji çıktılarını beslemez.
                break;
            }

            case Jt808ProtocolNumbers.TransparentUplink:
            {
                // 0x0900 tek başına shared profile eligibility anlamına gelmez.
                // Yalnız VL502/VG502 bu doğrulanmış Jimi F0 contract'ına bağlanır;
                // VL533/VL512/Unknown framing uygun olsa bile burada dışarıda kalır.
                if (!useJimiVehicleProfile)
                break;

                byte? subtype = _jimiJt808VehicleDataProfileParser.GetTransparentSubtype(frame);

                if (subtype == Jt808ProtocolNumbers.ObdSubtype)
                {
                        OBDDataModel obdData = GetParsedObdData(frame);

                        InputDataModel? parsedInput =
                            _jimiJt808VehicleDataProfileParser.ParseInputData(frame, obdData);

                        JimiJt808VehicleEnergyData vehicleEnergy =
                            _jimiJt808VehicleDataProfileParser.ParseVehicleEnergy(frame);

                        ParsedGpsMessage? gps =
                            _jimiJt808VehicleDataProfileParser.ParseTransparentGps(frame, obdData);

                        result.OBDData = obdData;

                        if (parsedInput is not null)
                        {
                            InputDataModel input = GetInputData(parsedInput);

                            result.InputData = input;

                            if (input.IsIgnitionOn.HasValue)
                                result.VehicleStatus =
                                    GetVehicleStatus(input.IsIgnitionOn.Value);
                        }
                        result.DateData = new DateDataModel { DeviceTimeUtc = obdData.DeviceTimeUtc };

                    if (vehicleEnergy.VehicleElectricalVoltageV.HasValue)
                        result.BatteryVoltage = GetParsedBatteryVoltage(vehicleEnergy);

                    result.BatteryCurrent = GetParsedBatteryCurrent(vehicleEnergy);

                    if (vehicleEnergy.BatteryLevel is not null)
                        result.BatteryLevel = GetParsedBatteryLevel(vehicleEnergy);

                    if (gps is not null)
                    {
                        result.GpsData = GetParsedGpsData(gps);
                        result.DateData = GetParsedDateData(gps);
                    }
                }
                else if (subtype == Jt808ProtocolNumbers.DtcSubtype)
                {
                        result.DTCFaultCodes = GetParsedDTCFaultCodes(frame);

                        InputDataModel? parsedInput =
                            _jimiJt808VehicleDataProfileParser.ParseInputData(frame);

                        ParsedGpsMessage? gps =
                            _jimiJt808VehicleDataProfileParser.ParseTransparentGps(frame);

                        if (parsedInput is not null)
                        {
                            InputDataModel input = GetInputData(parsedInput);

                            result.InputData = input;

                            if (input.IsIgnitionOn.HasValue)
                                result.VehicleStatus =
                                    GetVehicleStatus(input.IsIgnitionOn.Value);
                        }
                        
                        if (gps is not null)
                    {
                        result.GpsData = GetParsedGpsData(gps);
                        result.DateData = GetParsedDateData(gps);
                    }
                    else
                    {
                        result.DateData = _jimiJt808VehicleDataProfileParser.ParseTransparentDate(frame);
                    }
                }
                else if (subtype == Jt808ProtocolNumbers.VinSubtype)
                {
                    string vin = GetParsedVIN(_jimiJt808VehicleDataProfileParser.ParseVin(frame));
                    result.VIN = string.IsNullOrEmpty(vin) ? null : vin;
                    result.DateData = _jimiJt808VehicleDataProfileParser.ParseTransparentDate(frame);
                }

                // Tanınmayan shared-profile F0 subtype geçerli frame olarak kalır;
                // bu sürümde yalnız doğrulanmış alanlar doldurulur.
                break;
            }

            // 0x0002 heartbeat ve 0x0102 authentication body'leri proje
            // modellerine ek doğrulanmış alan sağlamaz.
            case Jt808ProtocolNumbers.Heartbeat:
            case Jt808ProtocolNumbers.Authenticate:
                break;

            default:
                break;
        }

        return result;
    }

    // ========================================================================
    // PRIVATE METOTLAR
    // ========================================================================

    private InputDataModel GetInputData(InputDataModel parsedInput)
    {
        ArgumentNullException.ThrowIfNull(parsedInput);
        return parsedInput;
    }

    private OBDDataModel GetParsedObdData(Jt808Frame frame)
    {
        // Görev sözleşmesindeki helper, doğrulanmış shared Jimi F0/01 public akışına
        // bağlanır. Generic JIMI 0x8C referans decoder'ı bundan ayrıdır.
        return _jimiJt808VehicleDataProfileParser.ParseObd(frame);
    }

    private string GetParsedVIN(string? parsedVin)
    {
        // Required project-contract helper remains on the functional VIN path.
        // Protocol-specific validation is completed before this aggregation step.
        return parsedVin ?? string.Empty;
    }

    private string GetParsedCCID(string? parsedCcid)
    {
        return IccidValidator.Normalize(parsedCcid) ?? string.Empty;
    }

    private string GetParsedOperatorCellularStatus(Jt808Frame frame)
    {
        // Proje sözleşmesindeki named helper, yalnız etkin shared profile altında
        // doğrulanmış 0x0200/E8 sonucunu aggregate'a taşır. Unsupported generic
        // JIMI 0x94/0x0B yolu bu metoda yönlendirilmez.
        return _jimiJt808VehicleDataProfileParser.ParseOperatorCellularStatus(frame) ?? string.Empty;
    }

    private decimal GetParsedBatteryVoltage(JimiJt808VehicleEnergyData vehicleEnergy)
    {
        // Nullable olmayan görev imzası korunur.
        // Bu yardımcı metot yalnız geçerli 0x0530 vehicle-electrical voltage bulunduğunda çağrılır.
        // Eksik bir değer için yapay olarak 0 üretilmez.
        if (!vehicleEnergy.VehicleElectricalVoltageV.HasValue)
            throw new InvalidOperationException("Jimi JT808 vehicle-energy mesajı geçerli vehicle-electrical voltaj değeri taşımıyor.");

        return vehicleEnergy.VehicleElectricalVoltageV.Value;
    }

    private decimal? GetParsedBatteryCurrent(JimiJt808VehicleEnergyData vehicleEnergy)
    {
        // 0x0704 NEV current ölçek, aralık ve sentinel kuralları shared profile içinde kalır.
        // Bu yardımcı metot yalnız doğrulanmış sonucu public modele aktarır.
        return vehicleEnergy.NevTotalCurrentA;
    }

    private int GetVehicleStatus(bool ignitionOn)
    {
        return ignitionOn ? 1 : 0;
    }

    private GpsDataModel GetParsedGpsData(ParsedGpsMessage parsedGps)
    {
        return parsedGps.Gps;
    }

    private DateDataModel GetParsedDateData(ParsedGpsMessage parsedGps)
    {
        return parsedGps.Date;
    }

    private BatteryDataModel GetParsedBatteryLevel(JimiJt808VehicleEnergyData vehicleEnergy)
    {
        // Non-nullable requirement imzası korunur; helper yalnız valid 0x0705 SOC
        // varsa çağrılır. 0530/0703-only veya generic device battery telemetrisi
        // BatteryLevel üretmez.
        return vehicleEnergy.BatteryLevel
               ?? throw new InvalidOperationException("Jimi JT808 vehicle-energy mesajı geçerli NEV SOC değeri taşımıyor.");
    }

    private List<string> GetParsedDTCFaultCodes(Jt808Frame frame)
    {
        // Yalnız doğrulanmış VL502/VG502 shared F0/02 DTC sözleşmesi bu helper'dan geçer.
        // Generic JIMI 0x65/0x66 payload'u doğrulanmadığı için public akışta
        // bu metoda yönlendirilmez.
        return _jimiJt808VehicleDataProfileParser.ParseDtc(frame);
    }
}
