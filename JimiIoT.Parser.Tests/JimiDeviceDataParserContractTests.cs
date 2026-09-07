using System.Reflection;
using JimiIoT.Parser.Models;
using JimiIoT.Parser;

namespace JimiIoT.Parser.Tests;

// ============================================================================
// Bu test davranış değil, GÖREV SÖZLEŞMESİNİ korur.
// Private metotları reflection ile çağırmıyoruz; yalnızca verilen isim,
// erişim ve dönüş tiplerinin refactor sırasında yanlışlıkla değişmediği kontrol edilir.
// ============================================================================
public class JimiDeviceDataParserContractTests
{
    [Fact]
    public void Class_And_Method_Signatures_Match_Project_Request()
    {
        Type type = typeof(JimiDeviceDataParser);

        Assert.True(type.IsPublic);
        Assert.False(type.IsSealed); // kullanıcı kararı: başlangıçta sealed olmayacak

        MethodInfo? publicMethod = type.GetMethod(
            "GetDeviceInfo",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: new[] { typeof(string) },
            modifiers: null);

        Assert.NotNull(publicMethod);
        Assert.Equal(typeof(DeviceInfoModel), publicMethod!.ReturnType);

        Assert.NotNull(typeof(DeviceInfoModel).GetProperty("ExtractedImei"));

        AssertPrivateReturnType(type, "GetInputData", typeof(InputDataModel));
        AssertPrivateReturnType(type, "GetParsedObdData", typeof(OBDDataModel));
        AssertPrivateReturnType(type, "GetParsedVIN", typeof(string));
        AssertPrivateReturnType(type, "GetParsedCCID", typeof(string));
        AssertPrivateReturnType(type, "GetParsedOperatorCellularStatus", typeof(string));
        AssertPrivateReturnType(type, "GetParsedBatteryVoltage", typeof(decimal));
        AssertPrivateReturnType(type, "GetParsedBatteryCurrent", typeof(decimal?));
        AssertPrivateReturnType(type, "GetVehicleStatus", typeof(int));
        AssertPrivateReturnType(type, "GetParsedGpsData", typeof(GpsDataModel));
        AssertPrivateReturnType(type, "GetParsedDateData", typeof(DateDataModel));
        AssertPrivateReturnType(type, "GetParsedBatteryLevel", typeof(BatteryDataModel));
        AssertPrivateReturnType(type, "GetParsedDTCFaultCodes", typeof(List<string>));
    }


    [Fact]
    public void JimiDeviceModel_Contains_All_Project_Models()
    {
        string[] names = Enum.GetNames<JimiDeviceModel>();

        foreach (string expected in new[]
                 {
                     "VL502", "VL533", "VG502", "VL512", "VL04",
                     "VG02U", "OB22", "VL505", "VL103D", "VL802"
                 })
        {
            Assert.Contains(expected, names);
        }
    }

    private static void AssertPrivateReturnType(Type type, string name, Type returnType)
    {
        MethodInfo? method = type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(method);
        Assert.True(method!.IsPrivate, $"{name} private olmalı.");
        Assert.Equal(returnType, method.ReturnType);
    }
}
