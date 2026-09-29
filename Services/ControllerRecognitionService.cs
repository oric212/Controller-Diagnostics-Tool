using HidSharp;

namespace Controller_Diagnostics_Tool.Services;

public sealed class ControllerRecognitionService
{
    public (string Model, string Family, bool Certain) Recognize(HidDevice device, string product)
    {
        var id = (device.VendorID, device.ProductID);
        if (id is (0x2DC8, 0x3106)) return ("8BitDo Receiver", "8BitDo", false);
        if (id is (0x054C, 0x05C4) or (0x054C, 0x09CC)) return ("DualShock 4", "PlayStation", true);
        if (id is (0x054C, 0x0CE6) or (0x054C, 0x0DF2)) return ("DualSense", "PlayStation", true);
        if (id is (0x057E, 0x2009)) return ("Switch Pro Controller", "Nintendo", true);
        if (id is (0x057E, 0x2006)) return ("Joy-Con (L)", "Nintendo", true);
        if (id is (0x057E, 0x2007)) return ("Joy-Con (R)", "Nintendo", true);

        var text = product.ToLowerInvariant();
        if (text.Contains("dualsense")) return ("DualSense", "PlayStation", false);
        if (text.Contains("dualshock")) return ("DualShock controller", "PlayStation", false);
        if (text.Contains("switch pro")) return ("Switch Pro Controller", "Nintendo", false);
        if (text.Contains("joy-con")) return ("Joy-Con", "Nintendo", false);
        if (device.VendorID == 0x045E || text.Contains("xbox")) return ("Xbox-compatible controller", "Xbox", false);
        if (device.VendorID == 0x054C) return ("PlayStation-compatible controller", "PlayStation", false);
        if (device.VendorID == 0x057E) return ("Nintendo-compatible controller", "Nintendo", false);
        return ("Generic HID Gamepad", "Generic HID", false);
    }
}
