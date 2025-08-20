using CommunityToolkit.Maui.Storage;
using Microsoft.Maui.Storage;
using Screenshot_Organiser;

namespace Screenshot_Organiser;

public partial class MainPage : ContentPage
{
    private readonly ModernScreenshotMonitor _monitor;
    private bool _hasPermissions = false;
    private bool _hasOverlayPermission = false;
    private int _screenshotCount = 0;

    public MainPage()
    {
        InitializeComponent();
        _monitor = new ModernScreenshotMonitor();
        _monitor.ScreenshotDetected += OnScreenshotDetected;
        CheckPermissions();
    }

    private async void CheckPermissions()
    {
        try
        {
            // Check modern Android permissions
            var photoPermission = await Permissions.CheckStatusAsync<Permissions.Photos>();
            var mediaPermission = await Permissions.CheckStatusAsync<Permissions.Media>();

            _hasPermissions = photoPermission == PermissionStatus.Granted ||
                             mediaPermission == PermissionStatus.Granted;

            // Check overlay permission
            _hasOverlayPermission = CheckOverlayPermission();

            UpdateUI();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Permission check failed: {ex.Message}", "OK");
        }
    }

    private bool CheckOverlayPermission()
    {
#if ANDROID
        if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.M)
        {
            return Android.Provider.Settings.CanDrawOverlays(Platform.CurrentActivity ?? Android.App.Application.Context);
        }
#endif
        return true;
    }

    private async void OnPermissionsClicked(object sender, EventArgs e)
    {
        try
        {
            // Request overlay permission first
            if (!_hasOverlayPermission)
            {
                await RequestOverlayPermission();
                return;
            }

            // Request storage permissions
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.R)
            {
                if (!Android.OS.Environment.IsExternalStorageManager)
                {
                    await DisplayAlert("Permission Required",
                        "Please enable 'Allow management of all files' in the next screen.", "OK");

                    var intent = new Android.Content.Intent(Android.Provider.Settings.ActionManageAppAllFilesAccessPermission);
                    intent.SetData(Android.Net.Uri.Parse($"package:{Android.App.Application.Context.PackageName}"));
                    Platform.CurrentActivity?.StartActivity(intent);
                }
            }

            // Request modern Android permissions
            var photoStatus = await Permissions.RequestAsync<Permissions.Photos>();
            var mediaStatus = await Permissions.RequestAsync<Permissions.Media>();

            _hasPermissions = photoStatus == PermissionStatus.Granted ||
                             mediaStatus == PermissionStatus.Granted;

            if (_hasPermissions && _hasOverlayPermission)
            {
                await DisplayAlert("Success", "All permissions granted! You can now start monitoring.", "OK");
            }
            else
            {
                await DisplayAlert("Permission Required",
                    "All permissions are required for the app to work properly.", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to request permissions: {ex.Message}", "OK");
        }

        // Recheck permissions
        await Task.Delay(1000);
        CheckPermissions();
    }

    private async Task RequestOverlayPermission()
    {
#if ANDROID
        try
        {
            await DisplayAlert("Overlay Permission Required",
                "This app needs permission to display over other apps to show screenshot dialogs. " +
                "Please enable this permission in the next screen.", "OK");

            var intent = new Android.Content.Intent(Android.Provider.Settings.ActionManageOverlayPermission);
            intent.SetData(Android.Net.Uri.Parse($"package:{Android.App.Application.Context.PackageName}"));
            Platform.CurrentActivity?.StartActivity(intent);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to open overlay permission settings: {ex.Message}", "OK");
        }
#endif
    }

    private async void OnStartMonitoringClicked(object sender, EventArgs e)
    {
        try
        {
            await _monitor.StartMonitoring();
            StatusLabel.Text = "Status: 👀 Monitoring active - Take a screenshot!";
            UpdateUI();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to start monitoring: {ex.Message}", "OK");
        }
    }

    private async void OnStopMonitoringClicked(object sender, EventArgs e)
    {
        try
        {
            await _monitor.StopMonitoring();
            StatusLabel.Text = "Status: ⏸️ Monitoring stopped";
            UpdateUI();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to stop monitoring: {ex.Message}", "OK");
        }
    }

    private void OnScreenshotDetected(object sender, ScreenshotEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _screenshotCount++;
            CountLabel.Text = $"Screenshots processed: {_screenshotCount}";
        });
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Recheck permissions when app becomes visible (user might have changed them)
        CheckPermissions();
    }

    private void UpdateUI()
    {
        var isMonitoring = _monitor.IsMonitoring;
        var hasAllPermissions = _hasPermissions && _hasOverlayPermission;

        StartMonitoringBtn.IsEnabled = hasAllPermissions && !isMonitoring;
        StopMonitoringBtn.IsEnabled = isMonitoring;

        StatusLabel.Text = (hasAllPermissions, isMonitoring) switch
        {
            (false, _) when !_hasOverlayPermission => "Status: 🔒 Overlay permission required",
            (false, _) when !_hasPermissions => "Status: 🔒 Storage permissions required",
            (true, false) => "Status: ✅ Ready to monitor",
            (true, true) => "Status: 👀 Monitoring active"
        };

        // Update permission button text
        PermissionsBtn.Text = hasAllPermissions ? "✅ Permissions Granted" : "🔐 Grant Permissions";
    }
}