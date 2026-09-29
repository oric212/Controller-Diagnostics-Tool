namespace Controller_Diagnostics_Tool.Models;

public sealed record ControllerDevice(
    string Name,
    string Manufacturer,
    string Kind,
    string VendorId,
    string ProductId,
    string DevicePath,
    string SerialNumber);
