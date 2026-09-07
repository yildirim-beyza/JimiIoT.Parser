using JimiIoT.Parser.Models;

namespace JimiIoT.Parser.Protocols.Profiles;

// Keeps model eligibility separate from packet parsing. Sharing this profile
// means sharing Jimi's verified JT808 vehicle-data packet contract, not treating
// the physical device models as interchangeable.
internal static class JimiJt808VehicleDataProfilePolicy
{
    internal static bool Supports(JimiDeviceModel model)
        => model is JimiDeviceModel.VL502 or JimiDeviceModel.VG502;
}
