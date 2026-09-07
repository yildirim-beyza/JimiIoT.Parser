namespace JimiIoT.Parser.Protocols.Profiles;

// F0 transparent envelope context defined by the VL502/VG502 V1.1.1 contract.
// Keep it internal: public project models do not need protocol-routing metadata.
internal enum JimiJt808VehicleDataType : byte
{
    Realtime = 0x00,
    Buffered = 0x01
}

internal enum JimiJt808VehicleType : byte
{
    Commercial = 0x01,
    Passenger = 0x02
}

internal readonly record struct JimiJt808VehicleDataContext(
    JimiJt808VehicleDataType DataType,
    JimiJt808VehicleType VehicleType,
    byte Subtype)
{
    internal bool IsCommercial => VehicleType == JimiJt808VehicleType.Commercial;
    internal bool IsPassenger => VehicleType == JimiJt808VehicleType.Passenger;
}
