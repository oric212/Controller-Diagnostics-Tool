using System.Windows;
using System.Windows.Controls;
using Controller_Diagnostics_Tool.Models;
using Controller_Diagnostics_Tool.Services;

namespace Controller_Diagnostics_Tool;

public partial class MainWindow : Window
{
    private readonly ControllerDiscoveryService _discovery = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        StatusText.Text = "Scanning HID devices…";
        try
        {
            var previousPath = (DeviceList.SelectedItem as ControllerDevice)?.DevicePath;
            var devices = await Task.Run(_discovery.Scan);
            DeviceList.ItemsSource = devices;
            DeviceList.SelectedItem = devices.FirstOrDefault(device => device.DevicePath == previousPath)
                ?? devices.FirstOrDefault();
            EmptyState.Visibility = devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = $"{devices.Count} controller{(devices.Count == 1 ? "" : "s")} found · Last scan {DateTime.Now:t}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not scan devices: {ex.Message}";
        }
    }

    private void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var device = DeviceList.SelectedItem as ControllerDevice;
        DetailsPanel.Visibility = device is null ? Visibility.Collapsed : Visibility.Visible;
        SelectionHint.Visibility = device is null ? Visibility.Visible : Visibility.Collapsed;
        if (device is null) { return; }

        DeviceName.Text = device.Name;
        DeviceKind.Text = device.Kind;
        Manufacturer.Text = device.Manufacturer;
        Identifiers.Text = $"{device.VendorId} / {device.ProductId}";
        SerialNumber.Text = string.IsNullOrWhiteSpace(device.SerialNumber) ? "Unavailable" : device.SerialNumber;
        DevicePath.Text = device.DevicePath;
    }
}
