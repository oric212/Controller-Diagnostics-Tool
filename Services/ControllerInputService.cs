using Controller_Diagnostics_Tool.Models;
using HidSharp;
using HidSharp.Reports;

namespace Controller_Diagnostics_Tool.Services;

public sealed class ControllerInputService : IDisposable
{
    private HidStream? _stream;
    private CancellationTokenSource? _stop;
    public event Action<IReadOnlyList<InputControl>>? Updated;
    public event Action<string>? StateChanged;

    public void Start(ControllerDevice device)
    {
        Stop();
        _stop = new CancellationTokenSource();
        var token = _stop.Token;
        _ = Task.Run(() => ReadLoop(device, token), token);
    }

    public void Stop()
    {
        _stop?.Cancel();
        _stream?.Dispose();
        _stream = null;
        _stop?.Dispose();
        _stop = null;
    }

    private void ReadLoop(ControllerDevice selected, CancellationToken token)
    {
        try
        {
            var device = DeviceList.Local.GetHidDevices().FirstOrDefault(d => d.DevicePath == selected.DevicePath);
            if (device is null) { StateChanged?.Invoke("Device disconnected"); return; }
            var descriptor = device.GetReportDescriptor();
            var parsers = descriptor.DeviceItems.Select(item => item.CreateDeviceItemInputParser()).ToArray();
            using var stream = device.Open();
            _stream = stream;
            stream.ReadTimeout = 500;
            StateChanged?.Invoke("Live input");
            var buffer = new byte[Math.Max(1, device.GetMaxInputReportLength())];
            var lastUpdate = DateTime.MinValue;
            while (!token.IsCancellationRequested)
            {
                int count;
                try { count = stream.Read(buffer, 0, buffer.Length); }
                catch (TimeoutException) { continue; }
                if (count <= 0) continue;
                var report = descriptor.InputReports.FirstOrDefault(r => r.ReportID == buffer[0]);
                if (report is null || count < report.Length) continue;
                var controls = new List<InputControl>();
                foreach (var parser in parsers)
                {
                    if (!parser.TryParseReport(buffer, 0, report)) continue;
                    for (var i = 0; i < parser.ValueCount; i++)
                    {
                        var value = parser.GetValue(i);
                        if (!value.IsValid || value.DataItem.IsConstant ||
                            (value.IsNull && !value.Usages.Contains((uint)Usage.GenericDesktopHatSwitch))) continue;
                        controls.Add(InputMappingService.Map(value, i, selected.Family));
                    }
                }
                if ((DateTime.UtcNow - lastUpdate).TotalMilliseconds < 33) continue;
                lastUpdate = DateTime.UtcNow;
                Updated?.Invoke(controls);
            }
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            StateChanged?.Invoke("Input unavailable: " + ex.Message);
        }
    }

    public void Dispose() => Stop();
}
