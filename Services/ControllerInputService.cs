using Controller_Diagnostics_Tool.Models;
using HidSharp;
using HidSharp.Reports;

namespace Controller_Diagnostics_Tool.Services;

public sealed class ControllerInputService : IDisposable
{
    private readonly object _gate = new();
    private HidStream? _stream;
    private CancellationTokenSource? _stop;
    public event Action<string, IReadOnlyList<InputControl>>? Updated;
    public event Action<string, string>? StateChanged;

    public void Start(ControllerDevice device)
    {
        Stop();
        var stop = new CancellationTokenSource();
        var token = stop.Token;
        lock (_gate) _stop = stop;
        _ = Task.Run(() => ReadLoop(device, token), token);
    }

    public void Stop()
    {
        HidStream? stream;
        CancellationTokenSource? stop;
        lock (_gate)
        {
            stop = _stop;
            stream = _stream;
            stop?.Cancel();
            _stream = null;
            _stop = null;
        }
        stream?.Dispose();
        stop?.Dispose();
    }

    private void ReadLoop(ControllerDevice selected, CancellationToken token)
    {
        try
        {
            var device = DeviceList.Local.GetHidDevices().FirstOrDefault(d => d.DevicePath == selected.DevicePath);
            if (device is null) { StateChanged?.Invoke(selected.DevicePath, "Device disconnected"); return; }
            var descriptor = device.GetReportDescriptor();
            var parsers = descriptor.DeviceItems.Select(item => item.CreateDeviceItemInputParser()).ToArray();
            if (token.IsCancellationRequested) return;
            using var stream = device.Open();
            lock (_gate)
            {
                if (token.IsCancellationRequested) return;
                _stream = stream;
            }
            try
            {
                stream.ReadTimeout = 500;
                StateChanged?.Invoke(selected.DevicePath, "Live input");
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
                    Updated?.Invoke(selected.DevicePath, controls);
                }
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_stream, stream)) _stream = null;
                }
            }
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
                StateChanged?.Invoke(selected.DevicePath, "Input unavailable: " + ex.Message);
        }
    }

    public void Dispose() => Stop();
}
