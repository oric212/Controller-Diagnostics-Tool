using System.Windows;
using System.Windows.Controls;
using Controller_Diagnostics_Tool.Models;
using Controller_Diagnostics_Tool.Services;

namespace Controller_Diagnostics_Tool;

public partial class MainWindow : Window
{
    private readonly ControllerDiscoveryService _discovery = new();
    private readonly ControllerInputService _input = new();

    public MainWindow()
    {
        InitializeComponent();
        DeviceName.Text = "Select a controller";
        _input.Updated += controls => Dispatcher.BeginInvoke(() => ShowInputs(controls));
        _input.StateChanged += state => Dispatcher.BeginInvoke(() => StatusText.Text = state);
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
        ShowInputs(Array.Empty<InputControl>());
        var device = DeviceList.SelectedItem as ControllerDevice;
        if (device is null)
        {
            DeviceName.Text = "Select a controller";
            FamilyText.Text = "";
            ConfidenceText.Text = "";
            DeviceInfo.Text = "";
            DevicePath.Text = "";
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
        ButtonControls.ItemsSource = controls.Where(c => c.Group is "Buttons" or "D-pad").ToArray();
        RawControls.ItemsSource = controls.Where(c => !c.Mapped).ToArray();
        MoveDot(LeftDot, controls, "Left stick X", "Left stick Y");
        MoveDot(RightDot, controls, "Right stick X", "Right stick Y");
        TriggerPanel.Children.Clear();
        if (!controls.Any(c => c.Group == "Triggers"))
            TriggerPanel.Children.Add(new TextBlock { Text = "No identified triggers", Foreground = System.Windows.Media.Brushes.SlateGray });
        foreach (var trigger in controls.Where(c => c.Group == "Triggers"))
        {
            TriggerPanel.Children.Add(new TextBlock { Text = trigger.Label + "  " + trigger.Display });
            TriggerPanel.Children.Add(new ProgressBar { Value = trigger.Value * 100, Maximum = 100, Height = 7, Margin = new Thickness(0, 5, 0, 10) });
        }
        if (controls.Count == 0) StatusText.Text = "Waiting for input reports...";
    }

    private static void MoveDot(UIElement dot, IReadOnlyList<InputControl> controls, string xName, string yName)
    {
        var x = controls.FirstOrDefault(c => c.Label == xName)?.Value ?? 0;
        var y = controls.FirstOrDefault(c => c.Label == yName)?.Value ?? 0;
        Canvas.SetLeft(dot, 44 + x * 38);
        Canvas.SetTop(dot, 44 + y * 38);
    }
}
