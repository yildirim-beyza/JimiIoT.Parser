# JimiIoT.Parser

**Hedef framework:** .NET 7 (`net7.0`)

`JimiIoT.Parser`, JIMI Protocol ve JT/T 808 mesajlarını tek giriş noktası üzerinden ayrıştıran bir C# kütüphanesidir.

Ana amaç, protokol çözümünü model-özel veri eşlemelerinden ayırmak ve yalnız doğrulanmış alanları public çıktılara taşımaktır.

## 1. Genel mimari

Kütüphane üç ana katmanla çalışır:

```text
generic JIMI / GT06
+
generic JT/T 808
+
shared Jimi JT808 vehicle-data profile (VL502 + VG502)
```

Akış özetle şöyledir:

```text
deviceData (HEX)
      |
      v
HexPayloadReader
      |
      v
ProtocolResolver
   /        \
  /          \
JIMI        JT/T 808
 |             |
 v             v
JimiFrame      Jt808Frame
Decoder        Decoder
 |             |
 v             v
JimiProtocol   Jt808ProtocolParser
Parser         (generic 0x0200)
                  |
                  +--> VL502 / VG502
                       JimiJt808VehicleDataProfileParser
```

`ProtocolResolver` cihaz modelini seçmez. Yalnız frame yapısına bakarak protokol ailesini belirler:

- `0x7878` / `0x7979` → JIMI Protocol
- `0x7E ... 0x7E` → JT/T 808

Model bilgisi framing işleminden sonra kullanılır. Böylece bir cihazın modeli ile kullandığı protokol birbirine karıştırılmaz.

## 2. Public kullanım

Ana sınıf:

```csharp
public class JimiDeviceDataParser
{
    public JimiDeviceDataParser();
    public JimiDeviceDataParser(JimiDeviceModel deviceModel);

    public DeviceInfoModel GetDeviceInfo(string deviceData);
}
```

Örnek:

```csharp
var parser = new JimiDeviceDataParser(JimiDeviceModel.VL502);
DeviceInfoModel result = parser.GetDeviceInfo(hexFrame);
```

Parametresiz constructor `JimiDeviceModel.Unknown` kullanır. Bu durumda generic protokol alanları çözülebilir ancak model-özel vehicle-data mapping otomatik uygulanmaz.

`GetDeviceInfo` tek bir tamamlanmış HEX frame bekler. Aşağıdaki işlemler bu sınıfın sorumluluğunda değildir:

- TCP stream fragmentation
- birden fazla frame'in stream içinden ayrılması
- JT/T 808 subpackage reassembly
- outbound command üretimi
- şifreli JT/T 808 body çözümü

Birleştirilmemiş subpackage veya şifreli JT/T 808 body bilinçli olarak reddedilir.

## 3. Proje isterindeki 13 metot

İstenen metot adları ve erişim sözleşmesi korunmuştur:

1. `GetDeviceInfo`
2. `GetInputData`
3. `GetParsedObdData`
4. `GetParsedVIN`
5. `GetParsedCCID`
6. `GetParsedOperatorCellularStatus`
7. `GetParsedBatteryVoltage`
8. `GetParsedBatteryCurrent`
9. `GetVehicleStatus`
10. `GetParsedGpsData`
11. `GetParsedDateData`
12. `GetParsedBatteryLevel`
13. `GetParsedDTCFaultCodes`

`GetDeviceInfo` public giriş metodudur. Diğer görev metotları ana sınıfın private akışında kullanılır.

## 4. Public çıktı modeli

`GetDeviceInfo` bir `DeviceInfoModel` döndürür. Her mesaj bütün alanları taşımadığı için alanların çoğu nullable'dır.

Başlıca çıktılar:

- `ExtractedImei`
- `ProtocolSerialNumber`
- `InputData`
- `OBDData`
- `VIN`
- `CCID`
- `OperatorCellularStatus`
- `BatteryVoltage`
- `BatteryCurrent`
- `VehicleStatus`
- `GpsData`
- `DateData`
- `BatteryLevel`
- `DTCFaultCodes`

Eksik veri ile gerçek `0` / `false` değeri birbirine karıştırılmaması için uygun alanlarda nullable tipler kullanılır.

## 5. JIMI Protocol kolu

Generic JIMI tarafında desteklenen temel işlemler:

- `0x7878` / `0x7979` framing
- CRC-16/X25 doğrulaması
- login mesajından BCD IMEI
- GPS ve cihaz zaman damgası
- `0x13` / `0x23` status çözümü
- `0x15` String Message üzerinden doğrulanmış ICCID
- `0x94/0A` Info yolu üzerinden ICCID

Kod içinde ayrıca bazı generic/reference decoder'lar bulunur:

- `0x8C` OBD key/value decoder
- `0x23`, `0x36` ve bazı `0x94` battery/voltage alanları
- `0x94/0B` network technology referans çözümü
- `0x65` / `0x66` DTC message ID'leri

Ancak bir decoder'ın kodda bulunması, o alanın bütün JIMI modelleri için public çıktı olarak doğrulandığı anlamına gelmez.

Özellikle generic JIMI battery/voltage değerleri, araç tarafı enerji verisi olduğu doğrulanmadığı için `BatteryVoltage`, `BatteryCurrent` veya `BatteryLevel` alanlarına taşınmaz.

### VL512

`JM-VL512 GPS Tracker Communication Protocol V1.0.0` için mevcut kodun sınırı şöyledir:

- generic JIMI login yolu kullanılabilir,
- generic JIMI GPS yolu kullanılabilir,
- `0x13` heartbeat/status yolu kullanılabilir,
- `0x94/0A` ICCID desteklenir,
- `0x94/00` external voltage bilgisi model dokümanında bulunsa da public vehicle `BatteryVoltage` olarak normalize edilmez,
- doğrulanmış direct OBD/VIN/DTC field map olmadığı için yeni VL512-specific OBD branch'i yoktur,
- `0x94/0B` değeri VL512 için doğrulanmış network technology sözleşmesi kabul edilmez.

## 6. Generic JT/T 808 kolu

`Jt808FrameDecoder` ve `Jt808ProtocolParser` modelden bağımsız temel JT/T 808 işlemlerini yapar:

- `0x7E` frame sınırları
- `0x7D01 -> 0x7D` ve `0x7D02 -> 0x7E` unescape
- XOR checksum
- message header ve body length
- subpackage metadata okuma
- generic 2011/2013 ve 2019 frame decoding
- standard `0x0200` location/status/time alanları
- `0x31` satellite count extension

Generic `0x0200` konum çözümü için VL502 veya VG502 seçmek gerekmez.

Generic JT/T 808 `0x61` / `0x69` voltage yardımcıları public vehicle battery kaynağı olarak kullanılmaz.

## 7. VL502 / VG502 shared Jimi JT808 vehicle-data profile

`JimiJt808VehicleDataProfileParser`, **Communication Protocol of VL502 & VG502 V1.1.1** içindeki ortak vehicle-data packet sözleşmesini uygular.

Profile eligibility açık şekilde şöyledir:

```text
VL502 -> aktif
VG502 -> aktif
VL533 -> pasif
VL512 -> pasif
Unknown -> pasif
```

Buradaki paylaşım, VL502 ve VG502 fiziksel olarak aynı cihazdır anlamına gelmez. Yalnız doğrulanmış ortak JT/T 808 vehicle-data packet contract aynı parser içinde tutulur.

Shared profile şu yolları işler:

- Terminal SN → IMEI
- `0x0100` registration içinden VIN
- `0x0104 / F007` ICCID
- `0x0104 / F025` VIN
- `0x0200 / E8` cellular network technology
- `0x0900 / F0 / 0x01` OBD ve vehicle-energy
- `0x0900 / F0 / 0x02` DTC
- `0x0900 / F0 / 0x0B` VIN
- F0 transparent tail içindeki status ve GPS

Shared V1.1.1 profile, JT/T 808-2013 tipindeki 6-byte Terminal SN sözleşmesine dayanır. Decoder generic 2019 header'ı okuyabilse de VL502/VG502 shared profile içinde 2019 versioned header kabul edilmez.

## 8. F0 context doğrulaması

`0x0900 / F0` mesajlarında yalnız subtype'a bakılmaz. Envelope içindeki context de doğrulanır:

### DataType

- `0x00` → Realtime
- `0x01` → Buffered
- diğer değerler → geçersiz

### VehicleType

- `0x01` → Commercial
- `0x02` → Passenger
- diğer değerler → geçersiz

Bazı OBD Field ID'leri yalnız ilgili vehicle type için semantic değere dönüştürülür. Yanlış context'te gelen alan ham olarak korunabilir ancak yanlış semantic çıktı üretilmez.

## 9. OBD alanları

`0x0900 / F0 / 0x01` içinde doğrulanmış başlıca alanlar:

| Field ID | Anlam | Dönüşüm / kural |
|---|---|---|
| `0x0102` | Commercial mileage | 4 byte, `raw / 10` km; yalnız Commercial |
| `0x0105` | Commercial fuel used | 4 byte, `raw / 100` L; yalnız Commercial |
| `0x0127` | Engine working time | 4 byte saniye, `/ 3600` saat; yalnız Commercial |
| `0x0522` | ACC Signal | 1 byte, yalnız `0` veya `1`; Passenger alanı |
| `0x0528` | Mileage | 4 byte, `raw / 10` km |
| `0x052B` | Fuel level | 1 byte, `0..100`; yalnız Passenger |
| `0x052C` | Fuel used | 4 byte, `raw / 100` L |
| `0x052D` | Coolant temperature | 1 byte, `raw - 40` °C; üst sınır kontrol edilir |
| `0x0535` | Speed | 2 byte, `raw / 10` km/h |
| `0x0536` | Engine speed | 2 byte RPM |
| `0x0544` | Fuel level | 1 byte, `0..100` |
| `0x0546` | Accumulated mileage | 4 byte, `raw / 10` km |

Bilinen bir Field ID yanlış uzunlukta veya geçersiz domain değeriyle gelirse semantic değer üretilmez. Ham veri `OBDData.RawFields` içinde HEX olarak korunur.

Bilinmeyen Field ID'ler de parser'ı durdurmaz; mümkün olduğunda `RawFields` içinde saklanır.

### ACC ve tail status

Geçerli F0/01 paketi sonunda zorunlu bir 12-byte tail taşır:

```text
Status DWORD
Latitude DWORD
Longitude DWORD
```

ACC iki farklı yerde bulunabilir:

- `0x0522` ACC Signal
- tail `Status` bit 0

`0x0522` yalnız `0` veya `1` domaininde semantic olarak geçerlidir. Geçersiz veya yanlış uzunluktaki `0x0522`, bağımsız ve geçerli tail status bilgisini bozmaz.

F0/01 için zorunlu tail eksikse veya tail sonrasında beklenmeyen ek veri varsa mesaj geçersiz kabul edilir.

## 10. Vehicle battery / energy semantiği

Public alanların anlamı özellikle ayrılmıştır:

- `BatteryVoltage` → conventional vehicle electrical voltage
- `BatteryCurrent` → doğrulanmış NEV total current
- `BatteryLevel` → doğrulanmış NEV SOC bilgisi

**Device backup battery bu public alanların kaynağı değildir.**

### Kullanılan Field ID'ler

| Field ID | Anlam | Kural | Public çıktı |
|---|---|---|---|
| `0x0530` | Vehicle electrical voltage | exact 2 byte, big-endian mV, `raw / 1000` V | `BatteryVoltage` |
| `0x0703` | NEV Total Voltage | exact 2 byte, `0..10000`, `raw * 0.1` V | tek başına `BatteryVoltage` üretmez |
| `0x0704` | NEV Total Current | exact 2 byte, `0..20000`, `raw * 0.1 - 1000` A | `BatteryCurrent` |
| `0x0705` | NEV Remaining Battery / SOC | exact 1 byte, `0..100` % | `BatteryLevel.LevelPercent` |

### 0x0530 ve 0x0703 birbirinin alternatifi değildir

`0x0530` conventional vehicle electrical voltage'tur.

`0x0703` ise NEV traction-system total voltage'tur. Bu nedenle `0x0703`, `0x0530` değerini override etmez.

Örnek:

```text
0x0530 = 12.66 V
0x0703 = 400.0 V

BatteryVoltage = 12.66 V
```

Sadece `0x0703` gelirse:

```text
BatteryVoltage = null
```

### BatteryLevel

`BatteryLevel` yalnız geçerli `0x0705` SOC bulunduğunda oluşturulur.

```text
0x0705 = 80
-> BatteryLevel.LevelPercent = 80
```

`0x0703` ile `0x0705` birlikte gelirse aynı NEV context içinde birleştirilebilir:

```text
0x0703 = 400.0 V
0x0705 = 80 %

BatteryVoltage = null
BatteryLevel.LevelPercent = 80
BatteryLevel.VoltageV = 400.0 V
```

`0x0530` ile `0x0705` birlikte gelirse farklı battery context'leri zorla birleştirilmez:

```text
0x0530 = 12.66 V
0x0705 = 80 %

BatteryVoltage = 12.66 V
BatteryLevel.LevelPercent = 80
BatteryLevel.VoltageV = null
```

`0x0703`, `0x0704` ve `0x0705` için geçersiz/sentinel değerler semantic çıktı üretmez. Ham Field ID değeri `RawFields` içinde korunur.

F0/07 capability mesajının önceden görülmesi runtime parsing için gerekli bir state gate değildir. Geçerli alan F0/01 paketinde mevcutsa doğrudan değerlendirilir.

## 11. CCID

### JIMI

Desteklenen yollar:

- `0x15` String Message
- `0x94/0A` Info

ICCID doğrulama katmanından geçmeden public sonuca aktarılmaz.

### VL502 / VG502 — `0x0104 / F007`

Shared profile'da ICCID, registration mesajından alınmaz.

`0x0104` Terminal Parameter Query Response içinde `0xF007` parametresi aranır. Parametre değeri içindeki token'lar anahtar adına göre okunur:

```text
IMEI:...;IMSI:...;ICCID:...;
```

Token sırasına güvenilmez. `ICCID:` değeri doğrulandıktan sonra public `CCID` alanına yazılır.

`F007` yoksa, parametre gövdesi bozuksa veya ICCID geçersizse sonuç `null` olur.

`0x0100` registration içindeki `Body[9..28]` alanı **Terminal Model** alanıdır; ICCID değildir.

Parser yalnız gelen `0x0104` response'u işler. `0x8106` query üretimi bu kütüphanenin kapsamında değildir.

## 12. OperatorCellularStatus

VL502/VG502 shared profile için kaynak `0x0200 / E8` vendor extension'dır.

```text
technology 0x2000 (GSM) -> "2G"
technology 0x2001 (LTE) -> "4G"
```

Şu durumlarda sonuç `null` olur:

- `E8` yoksa
- technology değeri bilinmiyorsa
- `E8` yapısal olarak geçersizse

`0x30` yalnız signal strength alanıdır. `0x30` üzerinden 2G/4G çıkarımı yapılmaz.

## 13. VIN

VL502/VG502 shared profile içinde doğrulanmış VIN yolları:

### `0x0100` Registration

Registration düzeni:

```text
Body[0..1]   Provincial ID
Body[2..3]   City/County ID
Body[4..8]   Manufacturer ID
Body[9..28]  Terminal Model
Body[29..35] Terminal ID
Body[36]     Plate Color
Body[37..]   Vehicle Identification
```

`Plate Color == 0` ise `Body[37..]` VIN olarak değerlendirilebilir.

### `0x0104 / F025`

V1.1.1 Table 4 içindeki `F025 STRING Vehicle VIN` desteklenir. Parametre ASCII ve geçerli VIN ise public `VIN` alanına aktarılır.

### `0x0900 / F0 / 0x0B`

VIN support flag yalnız `0x01` olduğunda devamındaki STRING VIN olarak değerlendirilir.

- `0x00` → VIN desteklenmiyor
- `0x01` → VIN parse edilir
- diğer değerler → VIN üretilmez

Wire payload 17 karakterden uzunsa sessizce kırpılmaz; geçersiz kabul edilir.

### `0xFEEC`

F0/01 içindeki `0xFEEC` alanı otomatik VIN alias'ı **değildir**.

Bu alan gelirse ham HEX değer `RawFields["FEEC"]` içinde korunabilir ancak public `VIN` üretilmez.

## 14. DTC

VL502/VG502 shared profile'da DTC yolu:

```text
0x0900 / F0 / 0x02
```

V1.1.1 dış zarfı ve record sayıları doğrulanır. Ancak 16-byte inner DTC record'un genel ARM layout'u mevcut kanıtlarla tamamen tanımlı değildir.

Bu nedenle parser bilinmeyen inner layout için tahmin yürütmez.

Mevcut davranış:

- doğrulanmış no-active-DTC mesajı → boş liste `[]`
- yapısal olarak bozuk/truncated DTC mesajı → exception
- genel J1939 byte'ları, ARM record mapping doğrulanmadan DTC koduna çevrilmez
- açık kaynakta gözlenen exact P1457 regression record'u → `P1457`
- bilinmeyen fakat yapısal olarak geçerli inner record → güvenli şekilde atlanır

Generic JIMI `0x65` / `0x66` için payload formatı yeterince doğrulanmadığından public DTC desteği etkin değildir.

## 15. GPS ve zaman

### Generic JIMI GPS

Desteklenen GPS mesaj ailesinden şu bilgiler çıkarılabilir:

- latitude
- longitude
- speed
- course
- GPS validity
- satellite count
- cihaz zaman damgası

### Generic JT/T 808 `0x0200`

Standart location/status alanları çözülür. `0x31` extension doğru uzunlukta varsa satellite count alınır.

### F0 transparent tail

F0 tail yalnız şu alanları doğrudan taşır:

- status
- latitude
- longitude

Bu yüzden tail içinde bulunmayan değerler uydurulmaz:

- speed yalnız aynı F0/01 paketinde doğrulanmış `0x0535` varsa doldurulur,
- course `null` kalabilir,
- satellite count `null` kalabilir.

### Zaman semantiği

Model property adı uyumluluk nedeniyle `DeviceTimeUtc` olarak korunmuştur. Ancak parser cihaz zamanını UTC'ye dönüştürmez.

Üretilen `DateTime.Kind`:

```text
Unspecified
```

Timezone / UTC normalizasyonu üst entegrasyon katmanının sorumluluğundadır.

## 16. Model scope

`JimiDeviceModel` içinde proje kapsamındaki modeller bulunur:

- `VL502`
- `VL533`
- `VG502`
- `VL512`
- `VL04`
- `VG02U`
- `OB22`
- `VL505`
- `VL103D`
- `VL802`
- `Unknown`

Aktif model-özel production profile durumu:

| Model | Davranış |
|---|---|
| `VL502` | shared VL502/VG502 Jimi JT808 vehicle-data profile aktif |
| `VG502` | shared VL502/VG502 Jimi JT808 vehicle-data profile aktif |
| `VL533` | shared F0 mapping uygulanmaz |
| `VL512` | generic JIMI yolları kullanılabilir; yeni model-specific OBD branch yok |
| `VL04` | generic protokol davranışı; doğrulanmış shared vehicle profile yok |
| `VG02U` | generic protokol davranışı; doğrulanmış shared vehicle profile yok |
| `OB22` | generic protokol davranışı; doğrulanmış shared vehicle profile yok |
| `VL505` | generic protokol davranışı; doğrulanmış shared vehicle profile yok |
| `VL103D` | generic protokol davranışı; doğrulanmış shared vehicle profile yok |
| `VL802` | generic protokol davranışı; doğrulanmış shared vehicle profile yok |
| `Unknown` | yalnız güvenli generic davranış; model-özel mapping yok |

Bir modelin tabloda bulunması, o model için bütün 13 public verinin desteklendiği anlamına gelmez. Semantic mapping yalnız kanıtlı protokol yollarında etkinleştirilir.

## 17. Bilinçli sınırlar

Bu sürümde bilinçli olarak yapılmayanlar:

- TCP stream birleştirme
- concatenated-frame orchestration
- JT/T 808 subpackage reassembly
- encrypted JT/T 808 body çözümü
- outbound `0x8106` command üretimi
- generic JIMI battery değerlerini vehicle battery kabul etme
- generic JT/T 808 `0x61/0x69` değerlerini vehicle battery kabul etme
- `0x30` signal strength üzerinden network technology tahmini
- exact kanıt olmadan VL533'e VL502/VG502 F0 mapping uygulama
- exact kanıt olmadan VL512 için direct OBD/VIN/DTC mapping uydurma
- `0xFEEC` alanını VIN diye varsayma
- bilinmeyen DTC inner record layout'undan kod tahmin etme

## 18. Test kapsamı

Test suite şu konuları kapsar:

- JIMI framing ve CRC
- JT/T 808 framing, unescape ve XOR
- IMEI
- GPS ve tarih
- status / vehicle status
- public 13-method contract
- model enum contract
- VL502/VG502 shared profile eligibility
- registration VIN
- `F007` ICCID
- `F025` VIN
- `E8` GSM/LTE mapping
- F0 OBD alanları
- DataType / VehicleType doğrulaması
- field length ve domain kontrolleri
- unknown field ve `RawFields` davranışı
- `0x0522` ACC domain kontrolü ve tail status
- `0x0530` vehicle electrical voltage
- `0x0703` NEV total voltage
- `0x0704` NEV total current
- `0x0705` SOC
- farklı battery context'lerinin birbirine karıştırılmaması
- VIN support flag
- `0xFEEC` alanının VIN'e dönüştürülmemesi
- DTC no-active / malformed / observed P1457 davranışları
- model isolation
- subpackage ve encryption rejection
- generic decoder verilerinin doğrulanmadan public vehicle alanlarına sızmaması

VG502 shared-profile testlerinin bir bölümü V1.1.1 ortak packet contract'tan üretilmiş sentetik fixture'lardır. Bunlar gerçek VG502 cihaz logu olarak sunulmaz; shared parser routing ve semantic eşitliği doğrulamak için kullanılır.

## 19. Final test sonucu

Bu source sürümündeki test inventory toplam **166 test case** içerir.

Kullanıcı tarafından gerçek .NET 7 ortamında doğrulanan final runtime sonucu:

```text
TOTAL:  166
PASSED: 166
FAILED: 0
```

Bu README hazırlanırken kullanılan çalışma ortamında `dotnet` kurulu olmadığı için testler burada yeniden çalıştırılmış gibi gösterilmemiştir. Yukarıdaki runtime sonucu kullanıcı execution evidence'ıdır.

## 20. Proje yapısı

```text
JimiIoT.Parser/
├── Conversion/
│   ├── BcdConverter.cs
│   ├── HexPayloadReader.cs
│   ├── IccidValidator.cs
│   ├── ScaleConverter.cs
│   └── VinValidator.cs
├── Fields/
│   └── StatusByteExtractor.cs
├── Framing/
│   ├── Crc16X25.cs
│   ├── DeviceFrame.cs
│   ├── JimiFrameDecoder.cs
│   ├── Jt808Frame.cs
│   └── Jt808FrameDecoder.cs
├── Models/
├── Protocols/
│   ├── JimiProtocolParser.cs
│   ├── Jt808ProtocolParser.cs
│   ├── ProtocolResolver.cs
│   └── Profiles/
│       ├── JimiJt808VehicleDataContext.cs
│       ├── JimiJt808VehicleDataProfileParser.cs
│       └── JimiJt808VehicleDataProfilePolicy.cs
└── JimiDeviceDataParser.cs

JimiIoT.Parser.Tests/
├── FramingTests.cs
├── JimiDeviceDataParserContractTests.cs
├── JimiDeviceDataParserTests.cs
├── JimiProtocolParserReferenceTests.cs
├── Jt808Tests.cs
└── TestFrameBuilder.cs
```

Ana ayrım nettir:

- `Framing` → frame sınırı ve checksum
- `Protocols` → protokol alanlarının çözümü
- `Profiles` → doğrulanmış vendor/model semantic sözleşmeleri
- `Conversion` / `Fields` → küçük dönüşüm ve bit yardımcıları
- `Models` → normalize public veri modelleri
- `JimiDeviceDataParser` → public facade ve sonuçların birleştirilmesi
