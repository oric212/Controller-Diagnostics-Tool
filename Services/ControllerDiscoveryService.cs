using Controller_Diagnostics_Tool.Models;
using HidSharp;
using HidSharp.Reports;

namespace Controller_Diagnostics_Tool.Services;

public sealed class ControllerDiscoveryService
{
    public IReadOnlyList<ControllerDevice> Scan()
    {
        var results = new List<ControllerDevice>();

        foreach (var device in DeviceList.Local.GetHidDevices())
        {
            try
            {
                var usages = device.GetReportDescriptor().DeviceItems
                    .SelectMany(item => item.Usages.GetAllValues())
                    .ToHashSet();

                var kind = usages.Contains((uint)Usage.GenericDesktopGamepad) ? "Gamepad"
                    : usages.Contains((uint)Usage.GenericDesktopJoystick) ? "Joystick"
                    : usages.Contains((uint)Usage.GenericDesktopMultiaxisController) ? "Multi-axis controller"
                    : null;

                if (kind is null) { continue; }

                string SafeRead(Func<string> read, string fallback)
                {
                    try { return read(); }
                    catch { return fallback; }
                }

                var name = SafeRead(() => device.GetFriendlyName(), "Unknown controller");
                results.Add(new ControllerDevice(
                    string.IsNullOrWhiteSpace(name) ? "Unknown controller" : name,
                    SafeRead(() => device.GetManufacturer(), "Unknown"),
                    kind,
                    $"0x{device.VendorID:X4}",
                    $"0x{device.ProductID:X4}",
                    device.DevicePath,
                    SafeRead(() => device.GetSerialNumber(), "Unavailable")));
            }
            catch
            {
                // A disconnected or inaccessible HID interface must not hide other devices.
            }
        }

        return results.OrderBy(device => device.Name).ThenBy(device => device.DevicePath).ToArray();
    }
}
