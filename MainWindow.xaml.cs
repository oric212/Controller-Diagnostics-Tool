using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Controller_Diagnostics_Tool.Models;
using Controller_Diagnostics_Tool.Services;

namespace Controller_Diagnostics_Tool;

public partial class MainWindow : Window
{
    private readonly ControllerDiscoveryService _discovery = new();
    private readonly ControllerInputService _input = new();
    private DiagnosticsSession _diagnostics = new();
    private IReadOnlyList<InputControl> _lastControls = Array.Empty<InputControl>();

    public MainWindow()
    {
        InitializeComponent();
        DeviceName.Text = "Select a controller";
        _input.Updated += (path, controls) => Dispatcher.BeginInvoke(() =>
        {
            if ((DeviceList.SelectedItem as ControllerDevice)?.DevicePath == path) ShowInputs(controls);
        });
        _input.StateChanged += (path, state) => Dispatcher.BeginInvoke(() =>
        {
            if ((DeviceList.SelectedItem as ControllerDevice)?.DevicePath == path) StatusText.Text = state;
        });
        Loaded += async (_, _) => await RefreshAsync();
        Closed += (_, _) => _input.Dispose();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        StatusText.Text = "Scanning HID devices...";
        try
        {
            var previous = (DeviceList.SelectedItem as ControllerDevice)?.DevicePath;
            var devices = await Task.Run(_discovery.Scan);
            DeviceList.ItemsSource = devices;
            DeviceList.SelectedItem = devices.FirstOrDefault(d => d.DevicePath == previous) ?? devices.FirstOrDefault();
            EmptyState.Visibility = devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = $"{devices.Count} controller(s) found - {DateTime.Now:t}";
        }
        catch (Exception ex) { StatusText.Text = "Scan failed: " + ex.Message; }
    }

    private void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _input.Stop();
        _diagnostics = new DiagnosticsSession();
        _diagnostics.Left.DeadZone = LeftDeadZone.Value;
        _diagnostics.Right.DeadZone = RightDeadZone.Value;
        _lastControls = Array.Empty<InputControl>();
        RenderInputs();
        var device = DeviceList.SelectedItem as ControllerDevice;
        if (device is null)
        {
            DeviceName.Text = "Select a controller";
            FamilyText.Text = ConfidenceText.Text = DeviceInfo.Text = DevicePath.Text = "";
            return;
        }
        DeviceName.Text = device.Model;
        FamilyText.Text = device.Family + "  /  " + device.Kind;
        ConfidenceText.Text = device.ModelIsCertain ? "Model matched by vendor and product ID" : "Family or model is an estimate";
        DeviceInfo.Text = $"{device.Name}  |  {device.Manufacturer}  |  VID {device.VendorId} / PID {device.ProductId}";
        DevicePath.Text = device.DevicePath;
        _input.Start(device);
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
        ButtonControls.ItemsSource = _lastControls.Where(c => c.Group is "Buttons" or "D-pad").ToArray();
        RawControls.ItemsSource = _lastControls.Where(c => !c.Mapped).Select(c =>
        {
            var range = _diagnostics.RawRanges.GetValueOrDefault(c.Label);
            return c with { Display = $"{c.Display}  [{range.Min:0.##} .. {range.Max:0.##}]" };
        }).ToArray();
        RenderStick("Left", _diagnostics.Left);
        RenderStick("Right", _diagnostics.Right);
        RenderTriggers();
        HistoryList.ItemsSource = _diagnostics.History.ToArray();
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

        dot.Opacity = stick.Available ? 1 : 0.4;
        Canvas.SetLeft(dot, 44 + stick.X * 38);
        Canvas.SetTop(dot, 44 + stick.Y * 38);
        var diameter = Math.Max(2, stick.DeadZone * 76);
        zone.Width = zone.Height = diameter;
        Canvas.SetLeft(zone, 52 - diameter / 2);
        Canvas.SetTop(zone, 52 - diameter / 2);

        state.Text = stick.Available ? stick.DriftStatus : "Waiting for input";
        state.Foreground = stick.DriftStatus switch
        {
            "Noticeable drift" => Brushes.OrangeRed,
            "Minor drift" => Brushes.Gold,
            "Stable" => Brushes.MediumTurquoise,
            _ => Brushes.LightSlateGray
        };
        position.Text = $"X {stick.X:+0.00;-0.00;0.00}  Y {stick.Y:+0.00;-0.00;0.00}  |  Radius {stick.Distance:0.00}";
        zoneText.Text = $"Dead zone {stick.DeadZone:P0}  |  {(stick.InsideDeadZone ? "Inside" : "Outside")}";
        neutral.Text = $"Resting center ({stick.NeutralX:+0.000;-0.000;0.000}, {stick.NeutralY:+0.000;-0.000;0.000})  |  Largest neutral deviation {stick.LargestNeutralDeviation:P1}";
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
    }

    private void DeadZone_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        _diagnostics.Left.DeadZone = LeftDeadZone.Value;
        _diagnostics.Right.DeadZone = RightDeadZone.Value;
        RenderStick("Left", _diagnostics.Left);
        RenderStick("Right", _diagnostics.Right);
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
