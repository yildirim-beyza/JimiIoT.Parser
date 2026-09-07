namespace JimiIoT.Parser.Models;

// ============================================================================
// JimiDeviceModel
// ----------------------------------------------------------------------------
// ProtocolResolver model seçmez; yalnız frame imzasından JIMI/JT808 kolunu
// belirler. Bu enum, framing sonrasında model-özel payload sözleşmesinin güvenli
// biçimde seçilebilmesi için üst entegrasyon katmanından gelen model bilgisidir.
//
// Unknown güvenli varsayılandır: generic protokol alanları çözülebilir fakat
// hiçbir model-özel OBD/CAN vendor mapping'i otomatik uygulanmaz.
// ============================================================================
public enum JimiDeviceModel
{
    Unknown = 0,
    VL502,
    VL533,
    VG502,
    VL512,
    VL04,
    VG02U,
    OB22,
    VL505,
    VL103D,
    VL802
}
