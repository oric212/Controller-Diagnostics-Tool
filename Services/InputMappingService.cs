using Controller_Diagnostics_Tool.Models;
using HidSharp.Reports;

namespace Controller_Diagnostics_Tool.Services;

public static class InputMappingService
{
    public static InputControl Map(DataValue value, int index, string family)
    {
        var usage = value.Usages.FirstOrDefault();
        var page = usage >> 16;
        var id = usage & 0xFFFF;
        var item = value.DataItem;
        var raw = value.IsNull ? item.LogicalMinimum - 1 : value.GetLogicalValue();
        var maximum = !item.IsLogicalSigned && item.LogicalMaximum < item.LogicalMinimum
            ? Math.Pow(2, item.ElementBits) - 1 : item.LogicalMaximum;
        var range = maximum - item.LogicalMinimum;
        var fraction = range > 0 ? Math.Clamp((raw - item.LogicalMinimum) / range, 0, 1) : 0;
        if (page == 9)
        {
            var number = id == 0 ? (uint)(index + 1) : id;
            return new InputControl("Button " + number, "Buttons", raw, raw == 0 ? "Released" : "Pressed", true, raw != 0);
        }
        if (usage == (uint)Usage.GenericDesktopHatSwitch)
        {
            var direction = raw - item.LogicalMinimum;
            var labels = new[] { "Up", "Up-right", "Right", "Down-right", "Down", "Down-left", "Left", "Up-left" };
            var display = direction >= 0 && direction < labels.Length ? labels[direction] : "Centered";
            return new InputControl("D-pad / hat", "D-pad", raw, display, true, display != "Centered");
        }
        string? label = usage switch
        {
            (uint)Usage.GenericDesktopX => "Left stick X",
            (uint)Usage.GenericDesktopY => "Left stick Y",
            (uint)Usage.GenericDesktopRx => "Right stick X",
            (uint)Usage.GenericDesktopRy => "Right stick Y",
            _ => null
        };
        if (label is not null)
            return new InputControl(label, "Sticks", fraction * 2 - 1, raw.ToString(), true, Math.Abs(fraction - 0.5) > 0.05);

        if (family == "Xbox" && usage is (uint)Usage.GenericDesktopZ or (uint)Usage.GenericDesktopRz)
        {
            var side = usage == (uint)Usage.GenericDesktopZ ? "Left" : "Right";
            return new InputControl(side + " trigger", "Triggers", fraction, $"{fraction:P0}", true, fraction > 0.05);
        }

        // Z, Rz, sliders and vendor usages vary between controllers; expose them without guessing.
        var rawLabel = usage == 0 ? "Field " + index : usage == (uint)Usage.GenericDesktopZ ? "Raw Z axis" : $"Usage 0x{page:X2}:0x{id:X2}";
        return new InputControl(rawLabel, "Raw axes / controls", fraction, raw.ToString(), false, raw != item.LogicalMinimum);
    }
}
