using System.Globalization;
using System.Text;
using Controller_Diagnostics_Tool.Models;

namespace Controller_Diagnostics_Tool.Services;

public static class DiagnosticsReport
{
    public static string Create(ControllerDevice device, DiagnosticsSession session,
        IReadOnlyList<InputControl> controls, string connectionState, DateTimeOffset timestamp)
    {
        var b = new StringBuilder();
        var culture = CultureInfo.InvariantCulture;
        static string Signed(double value) => value.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture);
        static string Range(double min, double max) =>
            double.IsInfinity(min) || double.IsInfinity(max) ? "Unavailable" : $"{Signed(min)} to {Signed(max)}";

        b.AppendLine("Controller Diagnostics Report");
        b.AppendLine("=============================");
        b.AppendLine($"Created: {timestamp:yyyy-MM-dd HH:mm:ss zzz}");
        b.AppendLine($"Controller: {device.Model}");
        b.AppendLine("Identification: " + (device.Family == "8BitDo" && device.Model.Contains("Receiver")
            ? "Receiver matched by VID/PID; paired controller model unknown"
            : device.ModelIsCertain ? "VID/PID model match" : "Estimated family or generic fallback"));
        b.AppendLine($"Family: {device.Family}");
        b.AppendLine($"Connection: {connectionState}");
        b.AppendLine($"Product: {device.Name}");
        b.AppendLine($"Manufacturer: {ValueOrUnavailable(device.Manufacturer)}");
        b.AppendLine($"Vendor ID: {device.VendorId}    Product ID: {device.ProductId}");
        b.AppendLine($"HID type: {device.Kind}");
        var battery = controls.FirstOrDefault(c => c.Group == "Battery" && c.Mapped);
        b.AppendLine($"Battery: {(battery is null ? "Unavailable" : battery.Value.ToString("P0", culture) + " (HID Battery Strength)")}");
        b.AppendLine($"Serial: {ValueOrUnavailable(device.SerialNumber)}");
        b.AppendLine($"Device path: {ValueOrUnavailable(device.DevicePath)}");
        b.AppendLine();

        var hasInput = controls.Count > 0;
        b.AppendLine("Controls");
        b.AppendLine("--------");
        b.AppendLine($"Buttons: {(hasInput ? controls.Count(c => c.Group == "Buttons").ToString(culture) : "Unavailable")}");
        b.AppendLine($"Mapped axes: {(hasInput ? controls.Count(c => c.Group is "Sticks" or "Triggers").ToString(culture) : "Unavailable")}");
        b.AppendLine($"Raw/unidentified controls: {(hasInput ? controls.Count(c => !c.Mapped).ToString(culture) : "Unavailable")}");
        b.AppendLine("Mapped controls: " + (hasInput
            ? (controls.Any(c => c.Mapped) ? string.Join(", ", controls.Where(c => c.Mapped).Select(c => c.Label).Distinct()) : "None mapped")
            : "Unavailable"));
        b.AppendLine();

        WriteStick(b, "Left stick", session.Left);
        WriteStick(b, "Right stick", session.Right);

        b.AppendLine("Triggers");
        b.AppendLine("--------");
        if (session.Triggers.Count == 0) b.AppendLine("No confidently identified analog triggers.");
        foreach (var (name, trigger) in session.Triggers)
        {
            b.AppendLine($"{name}: observed {Range(trigger.ObservedMin, trigger.ObservedMax)}");
            b.AppendLine($"  Range test: {Range(trigger.TestMin, trigger.TestMax)}");
        }
        b.AppendLine();

        b.AppendLine("Raw / unidentified controls");
        b.AppendLine("---------------------------");
        if (session.RawRanges.Count == 0) b.AppendLine("None observed.");
        foreach (var (name, range) in session.RawRanges.OrderBy(p => p.Key))
            b.AppendLine($"{name}: {Range(range.Min, range.Max)} (raw logical values)");
        b.AppendLine();

        b.AppendLine("Recent input");
        b.AppendLine("------------");
        if (session.History.Count == 0) b.AppendLine("No meaningful events recorded.");
        foreach (var item in session.History.Take(10))
            b.AppendLine($"{item.Time:HH:mm:ss}  {item.Control}: {item.State}");
        b.AppendLine();

        b.AppendLine("Limitations");
        b.AppendLine("-----------");
        b.AppendLine("Drift labels are indicative measurements from a short steady resting window; they do not diagnose a defect.");
        b.AppendLine("Dead-zone settings in this application are visual diagnostics and do not change the controller or Windows.");
        b.AppendLine("HID usages do not always reveal a physical control's role. Uncertain axes remain raw.");
        if (battery is null)
            b.AppendLine("Battery percentage was not exposed through the available HID input reports.");
        if (device.VendorId == "0x2DC8" && device.ProductId == "0x3106")
            b.AppendLine("This receiver combines LT and RT on one HID axis; simultaneous trigger pressure cannot be measured separately.");
        if (!device.ModelIsCertain)
            b.AppendLine("The exact physical controller model was not confirmed from the available device information.");
        return b.ToString();
    }

    private static void WriteStick(StringBuilder b, string name, StickDiagnostic stick)
    {
        static string Number(double value) => value.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture);
        static string Range(double min, double max) =>
            double.IsInfinity(min) || double.IsInfinity(max) ? "Unavailable" : $"{Number(min)} to {Number(max)}";
        b.AppendLine(name);
        b.AppendLine(new string('-', name.Length));
        b.AppendLine($"Status: {(stick.Available ? stick.HasNeutralEstimate ? stick.RestingStatus : "Not enough samples" : "Unmapped")}");
        b.AppendLine($"Dead zone: {(stick.DeadZone * 100).ToString("F0", CultureInfo.InvariantCulture) + "%"}");
        b.AppendLine($"Current position: {(stick.Available ? $"X {Number(stick.X)}, Y {Number(stick.Y)}, radius {stick.Distance:F3}" : "Unavailable")}");
        b.AppendLine($"Resting center: {(stick.HasNeutralEstimate ? $"X {Number(stick.NeutralX)}, Y {Number(stick.NeutralY)}" : "Unavailable")}");
        b.AppendLine($"Largest neutral deviation: {(stick.HasNeutralEstimate ? stick.LargestNeutralDeviation.ToString("P1", CultureInfo.InvariantCulture) : "Unavailable")}");
        b.AppendLine($"Observed X: {Range(stick.MinX, stick.MaxX)}");
        b.AppendLine($"Observed Y: {Range(stick.MinY, stick.MaxY)}");
        b.AppendLine($"Range test X: {Range(stick.TestMinX, stick.TestMaxX)}");
        b.AppendLine($"Range test Y: {Range(stick.TestMinY, stick.TestMaxY)}");
        b.AppendLine($"Range test center: {(double.IsInfinity(stick.TestMinX) ? "Unavailable" : $"X {Number(stick.TestCenterX)}, Y {Number(stick.TestCenterY)}")}");
        b.AppendLine($"Range test maximum radius: {(double.IsInfinity(stick.TestMinX) ? "Unavailable" : stick.TestMaxRadius.ToString("F3", CultureInfo.InvariantCulture))}");
        b.AppendLine();
    }

    private static string ValueOrUnavailable(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Unavailable" : value;
}
