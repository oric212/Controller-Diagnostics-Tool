using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text;
using System.IO;
using Microsoft.Win32;
using Controller_Diagnostics_Tool.Models;
using Controller_Diagnostics_Tool.Services;

namespace Controller_Diagnostics_Tool;

public partial class MainWindow : Window
{
    private readonly ControllerDiscoveryService _discovery = new();
    private readonly ControllerInputService _input = new();
    private DiagnosticsSession _diagnostics = new();
    private IReadOnlyList<InputControl> _lastControls = Array.Empty<InputControl>();
    private readonly DispatcherTimer _reconnectTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private string? _preferredPath;
    private ControllerDevice? _preferredDevice;
    private string? _activePath;
    private string _connectionState = "No device selected";
    private bool _updatingList;
    private bool _refreshing;
    private bool _closed;

    public MainWindow()
    {
        InitializeComponent();
        DeviceName.Text = "Select a controller";
        ResetAllButton.IsEnabled = ExportButton.IsEnabled = false;
        _input.Updated += (path, controls) => Dispatcher.BeginInvoke(() =>
        {
            if (!_closed && _activePath == path) ShowInputs(controls);
        });
        _input.StateChanged += (path, state) => Dispatcher.BeginInvoke(() =>
        {
            if (_closed || _activePath != path) return;
            _connectionState = state;
            StatusText.Text = state;
            if (state == "Live input") _reconnectTimer.Stop();
            else if (state.StartsWith("Input unavailable") || state == "Device disconnected")
                _reconnectTimer.Start();
            RenderSummary();
        });
        _reconnectTimer.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) => await RefreshAsync();
        Closed += (_, _) => { _closed = true; _reconnectTimer.Stop(); _input.Dispose(); };
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_refreshing || _closed) return;
        _refreshing = true;
        try
        {
            var devices = await Task.Run(_discovery.Scan);
            if (_closed) return;
            var wanted = _preferredPath;
            var currentPaths = (DeviceList.ItemsSource as IEnumerable<ControllerDevice>)?.Select(d => d.DevicePath);
            if (currentPaths is null || !currentPaths.SequenceEqual(devices.Select(d => d.DevicePath)))
            {
                _updatingList = true;
                DeviceList.ItemsSource = devices;
                var selected = wanted is null ? devices.FirstOrDefault()
                    : devices.FirstOrDefault(d => d.DevicePath == wanted);
                if (selected is not null) { _preferredPath = selected.DevicePath; _preferredDevice = selected; }
                DeviceList.SelectedItem = selected;
                _updatingList = false;
                if (selected?.DevicePath != _activePath) SelectDevice(selected);
            }
            else if (_connectionState.StartsWith("Input unavailable") && DeviceList.SelectedItem is ControllerDevice retry)
            {
                _connectionState = "Connecting";
                _input.Start(retry);
                RenderSummary();
            }
            EmptyState.Visibility = devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_connectionState == "No device selected" || _connectionState == "Connecting")
                StatusText.Text = $"{devices.Count} controller(s) found - {DateTime.Now:t}";
        }
        catch (Exception ex) { StatusText.Text = "Scan failed: " + ex.Message; }
        finally { _updatingList = false; _refreshing = false; }
    }

    private void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingList) return;
        var device = DeviceList.SelectedItem as ControllerDevice;
        _preferredPath = device?.DevicePath;
        _preferredDevice = device;
        _reconnectTimer.Stop();
        SelectDevice(device);
    }

    private void SelectDevice(ControllerDevice? device)
    {
        _input.Stop();
        _activePath = device?.DevicePath;
        _diagnostics = new DiagnosticsSession();
        _diagnostics.Left.DeadZone = LeftDeadZone.Value;
        _diagnostics.Right.DeadZone = RightDeadZone.Value;
        _lastControls = Array.Empty<InputControl>();
        _connectionState = device is null
            ? (_preferredPath is null ? "No device selected" : "Disconnected - waiting for reconnect")
            : "Connecting";
        DeviceName.Text = device?.Model ?? (_preferredDevice is null ? "Select a controller" : _preferredDevice.Model + " (disconnected)");
        FamilyText.Text = device is null
            ? (_preferredDevice is null ? "" : _preferredDevice.Family + "  /  " + _preferredDevice.Kind)
            : device.Family + "  /  " + device.Kind;
        ConfidenceText.Text = device is null ? "" : device.Family == "8BitDo" && device.Model.Contains("Receiver") ? "Receiver identified; paired controller model unknown" : device.ModelIsCertain
            ? "Model matched by vendor and product ID" : "Family or model is an estimate";
        DeviceInfo.Text = device is null ? "" : $"{device.Name}  |  {device.Manufacturer}  |  VID {device.VendorId} / PID {device.ProductId}";
        DevicePath.Text = device?.DevicePath ?? "";
        CombinedTriggerNote.Visibility = device?.VendorId == "0x2DC8" && device.ProductId == "0x3106"
            ? Visibility.Visible : Visibility.Collapsed;
        ResetAllButton.IsEnabled = ExportButton.IsEnabled = device is not null;
        RenderInputs();
        if (device is not null) _input.Start(device);
    }

    private void ShowInputs(IReadOnlyList<InputControl> controls)
    {
        _lastControls = controls;
        _diagnostics.Update(controls);
        RenderInputs();
        if (controls.Count == 0) StatusText.Text = "Waiting for input reports...";
    }

    private void RenderInputs()
    {
        RenderButtons();
        RawControls.ItemsSource = _lastControls.Where(c => !c.Mapped).Select(c =>
        {
            return _diagnostics.RawRanges.TryGetValue(c.Label, out var range)
                ? c with { Display = $"{c.Display}  [{range.Min:0.##} .. {range.Max:0.##}]" }
                : c with { Display = c.Display + "  [range pending]" };
        }).ToArray();
        RenderStick("Left", _diagnostics.Left);
        RenderStick("Right", _diagnostics.Right);
        RenderControllerSticks();
        RenderTriggers();
        HistoryList.ItemsSource = _diagnostics.History.ToArray();
        RenderSummary();
    }

    private static readonly (string Label, double X, double Y, string Accent)[] XboxFaceLayout =
    [
        ("A", 309, 120, "#6FCD78"), ("B", 336, 93, "#EC6669"),
        ("X", 281, 93, "#52A9EC"), ("Y", 309, 66, "#F0CF58")
    ];

    private static readonly Brush DpadNeutral = new SolidColorBrush(Color.FromRgb(141, 147, 152));
    private static readonly Brush ButtonNeutral = new SolidColorBrush(Color.FromRgb(114, 121, 128));
    private static readonly Brush SmallButtonNeutral = new SolidColorBrush(Color.FromRgb(133, 139, 145));
    private static readonly Brush StickNeutral = new SolidColorBrush(Color.FromRgb(137, 143, 148));

    private void RenderButtons()
    {
        var device = DeviceList.SelectedItem as ControllerDevice;
        var verifiedReceiver = device is not null && device.VendorId == "0x2DC8" && device.ProductId == "0x3106";
        var xboxLabels = verifiedReceiver || device?.Family == "Xbox";
        ButtonLayoutNote.Text = verifiedReceiver
            ? "Xbox layout; this receiver's button order was checked with the paired controller."
            : device?.Family == "Xbox"
                ? "Xbox layout using common HID button order; individual devices may vary."
                : "Xbox-style view; numbered HID buttons have illustrative positions.";

        var dpad = _lastControls.FirstOrDefault(c => c.Group == "D-pad")?.Display ?? "";
        LayoutDpadUp.Fill = dpad.Contains("Up", StringComparison.OrdinalIgnoreCase) ? Brushes.MediumTurquoise : DpadNeutral;
        LayoutDpadRight.Fill = dpad.Contains("Right", StringComparison.OrdinalIgnoreCase) ? Brushes.MediumTurquoise : DpadNeutral;
        LayoutDpadDown.Fill = dpad.Contains("Down", StringComparison.OrdinalIgnoreCase) ? Brushes.MediumTurquoise : DpadNeutral;
        LayoutDpadLeft.Fill = dpad.Contains("Left", StringComparison.OrdinalIgnoreCase) ? Brushes.MediumTurquoise : DpadNeutral;

        bool Pressed(int number) => _lastControls.Any(c => c.Label == "Button " + number && c.Active);
        LayoutLB.Background = xboxLabels && Pressed(5) ? Brushes.MediumTurquoise : ButtonNeutral;
        LayoutRB.Background = xboxLabels && Pressed(6) ? Brushes.MediumTurquoise : ButtonNeutral;
        LayoutView.Fill = xboxLabels && Pressed(7) ? Brushes.MediumTurquoise : SmallButtonNeutral;
        LayoutMenu.Fill = xboxLabels && Pressed(8) ? Brushes.MediumTurquoise : SmallButtonNeutral;
        LayoutLeftStickHead.Background = xboxLabels && Pressed(9) ? Brushes.MediumTurquoise : StickNeutral;
        LayoutRightStickHead.Background = xboxLabels && Pressed(10) ? Brushes.MediumTurquoise : StickNeutral;
        LayoutLeftStickText.Text = xboxLabels ? "LS" : "";
        LayoutRightStickText.Text = xboxLabels ? "RS" : "";

        var indicators = new List<ControllerButtonIndicator>();
        var extras = new List<ControllerButtonIndicator>();
        foreach (var button in _lastControls.Where(c => c.Group == "Buttons"))
        {
            var rawNumber = button.Label.Replace("Button ", "");
            if (!int.TryParse(rawNumber, out var number) || number < 1)
            {
                extras.Add(new(rawNumber, button.Label + ": " + button.Display, button.Active, 0, 0, "#899198"));
                continue;
            }
            if (number <= XboxFaceLayout.Length)
            {
                var (mappedLabel, x, y, accent) = XboxFaceLayout[number - 1];
                var label = xboxLabels ? mappedLabel : rawNumber;
                var detail = xboxLabels ? mappedLabel + " (" + button.Label + "): " + button.Display
                    : button.Label + ": " + button.Display;
                indicators.Add(new(label, detail, button.Active, x, y, xboxLabels ? accent : "#899198"));
            }
            else if (!xboxLabels || number > 10)
                extras.Add(new(rawNumber, button.Label + ": " + button.Display, button.Active, 0, 0, "#899198"));
        }
        ButtonControls.ItemsSource = indicators;
        ExtraButtons.ItemsSource = extras;
    }

    private void RenderControllerSticks()
    {
        Canvas.SetLeft(LayoutLeftStickHead, 84 + _diagnostics.Left.X * 5);
        Canvas.SetTop(LayoutLeftStickHead, 78 + _diagnostics.Left.Y * 5);
        Canvas.SetLeft(LayoutRightStickHead, 242 + _diagnostics.Right.X * 5);
        Canvas.SetTop(LayoutRightStickHead, 136 + _diagnostics.Right.Y * 5);
        LayoutLeftStickHead.Opacity = _diagnostics.Left.Available && _connectionState == "Live input" ? 1 : 0.55;
        LayoutRightStickHead.Opacity = _diagnostics.Right.Available && _connectionState == "Live input" ? 1 : 0.55;
    }

    private void RenderStick(string side, StickDiagnostic stick)
    {
        var left = side == "Left";
        var dot = left ? LeftDot : RightDot;
        var zone = left ? LeftZone : RightZone;
        var state = left ? LeftState : RightState;
        var position = left ? LeftPosition : RightPosition;
        var zoneText = left ? LeftZoneText : RightZoneText;
        var neutral = left ? LeftNeutral : RightNeutral;
        var observed = left ? LeftObserved : RightObserved;
        var test = left ? LeftTest : RightTest;
        var start = left ? LeftStartButton : RightStartButton;

        dot.Opacity = stick.Available && _connectionState == "Live input" ? 1 : 0.4;
        Canvas.SetLeft(dot, 44 + stick.X * 38);
        Canvas.SetTop(dot, 44 + stick.Y * 38);
        var diameter = Math.Max(2, stick.DeadZone * 76);
        zone.Width = zone.Height = diameter;
        Canvas.SetLeft(zone, 52 - diameter / 2);
        Canvas.SetTop(zone, 52 - diameter / 2);

        state.Text = stick.Available ? stick.DriftStatus : "Waiting for input";
        state.Foreground = stick.DriftStatus switch
        {
            "Noticeable drift observed" => Brushes.OrangeRed,
            "Minor drift observed" => Brushes.Gold,
            "Stable" => Brushes.MediumTurquoise,
            _ => Brushes.LightSlateGray
        };
        position.Text = $"X {stick.X:+0.00;-0.00;0.00}  Y {stick.Y:+0.00;-0.00;0.00}  |  Radius {stick.Distance:0.00}";
        zoneText.Text = $"Dead zone {stick.DeadZone:P0}  |  {(stick.InsideDeadZone ? "Inside" : "Outside")}";
        neutral.Text = stick.HasNeutralEstimate
            ? $"Resting center ({stick.NeutralX:+0.000;-0.000;0.000}, {stick.NeutralY:+0.000;-0.000;0.000})  |  Largest neutral deviation {stick.LargestNeutralDeviation:P1}"
            : "Resting center: Not enough samples";
        observed.Text = stick.Available
            ? $"Observed X {stick.MinX:+0.00;-0.00;0.00} .. {stick.MaxX:+0.00;-0.00;0.00}   Y {stick.MinY:+0.00;-0.00;0.00} .. {stick.MaxY:+0.00;-0.00;0.00}"
            : "Observed range: waiting";
        test.Text = double.IsInfinity(stick.TestMinX) ? "Range test: not started"
            : $"Test center ({stick.TestCenterX:+0.00;-0.00;0.00}, {stick.TestCenterY:+0.00;-0.00;0.00})  |  Max radius {stick.TestMaxRadius:0.00}\nX {stick.TestMinX:+0.00;-0.00;0.00} .. {stick.TestMaxX:+0.00;-0.00;0.00}   Y {stick.TestMinY:+0.00;-0.00;0.00} .. {stick.TestMaxY:+0.00;-0.00;0.00}";
        start.Content = stick.Testing ? "Stop range test" : "Start range test";
    }

    private void RenderTriggers()
    {
        TriggerPanel.Children.Clear();
        if (_diagnostics.Triggers.Count == 0)
            TriggerPanel.Children.Add(new TextBlock { Text = "No identified triggers", Foreground = Brushes.LightSlateGray });
        foreach (var (name, trigger) in _diagnostics.Triggers)
        {
            TriggerPanel.Children.Add(new TextBlock
            {
                Text = $"{name}  {trigger.Value:P0}  |  observed {trigger.ObservedMin:P0} .. {trigger.ObservedMax:P0}",
                Foreground = Brushes.White
            });
            TriggerPanel.Children.Add(new ProgressBar
            {
                Value = trigger.Value * 100, Maximum = 100, Height = 7,
                Margin = new Thickness(0, 5, 0, 5)
            });
            if (!double.IsInfinity(trigger.TestMin))
                TriggerPanel.Children.Add(new TextBlock
                {
                    Text = $"Test range {trigger.TestMin:P0} .. {trigger.TestMax:P0}",
                    Foreground = Brushes.LightSlateGray,
                    FontSize = 11
                });
        }
        TriggerStartButton.Content = _diagnostics.Triggers.Values.Any(t => t.Testing) ? "Stop trigger test" : "Start trigger test";
        TriggerStartButton.IsEnabled = _diagnostics.Triggers.Count > 0;
        RenderControllerTriggers();
    }

    private void RenderControllerTriggers()
    {
        _diagnostics.Triggers.TryGetValue("Left trigger", out var left);
        _diagnostics.Triggers.TryGetValue("Right trigger", out var right);
        LayoutLTLevel.Width = 80 * (left?.Value ?? 0);
        LayoutRTLevel.Width = 80 * (right?.Value ?? 0);
        LayoutLTFrame.Opacity = left is null ? 0.5 : 1;
        LayoutRTFrame.Opacity = right is null ? 0.5 : 1;
        LayoutLTFrame.ToolTip = left is null ? "LT: no mapped input" : $"LT: {left.Value:P0}";
        LayoutRTFrame.ToolTip = right is null ? "RT: no mapped input" : $"RT: {right.Value:P0}";
    }

    private void RenderBattery()
    {
        var battery = _lastControls.FirstOrDefault(c => c.Group == "Battery" && c.Mapped);
        var available = battery is not null && _connectionState == "Live input";
        var fraction = available ? Math.Clamp(battery!.Value, 0, 1) : 0;
        LayoutBatteryFill.Width = 61 * fraction;
        LayoutBatteryText.Text = available ? $"{fraction:P0}" : "N/A";
        LayoutBatteryFrame.ToolTip = available
            ? $"Battery strength reported by HID: {fraction:P0}"
            : "Battery percentage unavailable from this device";
    }
    private void RenderSummary()
    {
        var device = DeviceList.SelectedItem as ControllerDevice;
        SummaryModel.Text = device is null
            ? (_preferredPath is null ? "No controller selected" : "Waiting for controller to reconnect")
            : $"{device.Model}  |  {device.VendorId} / {device.ProductId}";
        SummaryConnection.Text = "Connection: " + _connectionState;
        SummaryConnection.Foreground = _connectionState == "Live input" ? Brushes.MediumTurquoise
            : _connectionState.StartsWith("Input unavailable") || _connectionState.Contains("Disconnected")
                ? Brushes.OrangeRed : Brushes.LightSlateGray;

        var hasInput = _lastControls.Count > 0;
        var buttons = _lastControls.Count(c => c.Group == "Buttons");
        var axes = _lastControls.Count(c => c.Group is "Sticks" or "Triggers");
        var raw = _lastControls.Count(c => !c.Mapped);
        SummaryControls.Text = hasInput
            ? $"{buttons} buttons  |  {axes} mapped axes  |  {raw} raw/unidentified"
            : "Controls: Unavailable until input arrives";
        SummaryLeft.Text = "Left stick: " + StickStatus(_diagnostics.Left);
        SummaryRight.Text = "Right stick: " + StickStatus(_diagnostics.Right);
        SummaryDeadZones.Text = $"Dead zones: L {_diagnostics.Left.DeadZone:P0}  |  R {_diagnostics.Right.DeadZone:P0}";
        SummaryRanges.Text = $"Observed left: {StickRange(_diagnostics.Left)}\nObserved right: {StickRange(_diagnostics.Right)}";
        SummaryTriggers.Text = _diagnostics.Triggers.Count == 0 ? "Trigger ranges: Unmapped"
            : "Trigger ranges: " + string.Join("; ", _diagnostics.Triggers.Select(pair =>
                $"{pair.Key} {pair.Value.ObservedMin:P0}..{pair.Value.ObservedMax:P0}"));
    }

    private static string StickStatus(StickDiagnostic stick) =>
        !stick.Available ? "Unmapped" : stick.HasNeutralEstimate ? stick.RestingStatus : "Not enough samples";

    private static string StickRange(StickDiagnostic stick) => !stick.Available ? "Unavailable"
        : $"X {stick.MinX:+0.00;-0.00;0.00}..{stick.MaxX:+0.00;-0.00;0.00}, Y {stick.MinY:+0.00;-0.00;0.00}..{stick.MaxY:+0.00;-0.00;0.00}";

    private void ResetMeasurements_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItem is not ControllerDevice) return;
        _diagnostics = new DiagnosticsSession();
        _diagnostics.Left.DeadZone = LeftDeadZone.Value;
        _diagnostics.Right.DeadZone = RightDeadZone.Value;
        RenderInputs();
        StatusText.Text = "Measurements reset; collecting new samples.";
    }

    private void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItem is not ControllerDevice device) return;
        var dialog = new SaveFileDialog
        {
            Title = "Save diagnostics report",
            Filter = "Text report (*.txt)|*.txt",
            DefaultExt = ".txt",
            AddExtension = true,
            FileName = $"Controller-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var report = DiagnosticsReport.Create(device, _diagnostics, _lastControls, _connectionState, DateTimeOffset.Now);
            File.WriteAllText(dialog.FileName, report, new UTF8Encoding(false));
            StatusText.Text = "Report saved: " + dialog.FileName;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not save the report: " + ex.Message,
                "Export report", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeadZone_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        _diagnostics.Left.DeadZone = LeftDeadZone.Value;
        _diagnostics.Right.DeadZone = RightDeadZone.Value;
        RenderStick("Left", _diagnostics.Left);
        RenderStick("Right", _diagnostics.Right);
        RenderControllerSticks();
        RenderSummary();
    }
    private void StickTest_Click(object sender, RoutedEventArgs e)
    {
        var stick = (sender as Button)?.Tag?.ToString() == "Left" ? _diagnostics.Left : _diagnostics.Right;
        stick.ToggleTest();
        RenderInputs();
    }

    private void StickReset_Click(object sender, RoutedEventArgs e)
    {
        var stick = (sender as Button)?.Tag?.ToString() == "Left" ? _diagnostics.Left : _diagnostics.Right;
        stick.ResetTest();
        RenderInputs();
    }

    private void TriggerTest_Click(object sender, RoutedEventArgs e)
    {
        foreach (var trigger in _diagnostics.Triggers.Values) trigger.ToggleTest();
        RenderTriggers();
    }

    private void TriggerReset_Click(object sender, RoutedEventArgs e)
    {
        foreach (var trigger in _diagnostics.Triggers.Values) trigger.Reset();
        RenderTriggers();
    }
}

public sealed record ControllerButtonIndicator(string Label, string Detail, bool Active, double X, double Y, string Accent);
