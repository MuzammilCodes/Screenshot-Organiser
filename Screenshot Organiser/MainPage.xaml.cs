namespace Screenshot_Organiser;

public partial class MainPage : ContentPage
{
    private readonly ModernScreenshotMonitor _monitor;
    private bool _hasPermissions = false;
    private bool _hasOverlayPermission = false;
    private bool _permissionsRequested = false;
    private bool _overlayPermissionRequested = false;
    private bool _filePermissionRequested = false;
    private bool _initialLoadComplete = false;

    public MainPage()
    {
        InitializeComponent();
        _monitor = new ModernScreenshotMonitor();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Only run initial setup once
        if (!_initialLoadComplete)
        {
            _initialLoadComplete = true;

            // Wait for UI to fully load
            await Task.Delay(1000);

            if (!_permissionsRequested)
            {
                _permissionsRequested = true;
                await RequestPermissionsSequentially();
            }
        }
    }

    // This method will be called by the lifecycle event when app resumes
    public async void OnAppResumed()
    {
        try
        {
            Console.WriteLine("🔄 MainPage handling app resume");

            // Only process if we've already started the permission flow
            if (_permissionsRequested)
            {
                await CheckPermissionsAndContinueFlow();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in OnAppResumed: {ex}");
        }
    }

    private async Task RequestPermissionsSequentially()
    {
        try
        {
            StatusLabel.Text = "Status: 🔄 Checking permissions...";

            // Step 1: Check current permissions
            await CheckPermissions();
            await Task.Delay(500);

            // Step 2: Request overlay permission if not granted
            if (!_hasOverlayPermission && !_overlayPermissionRequested)
            {
                StatusLabel.Text = "Status: 🔒 Opening overlay permission settings...";
                await Task.Delay(500);
                await OpenOverlayPermissionSettings();
                return; // Exit here, will continue when user returns to app
            }

            // Step 3: Request file access permissions if not granted
            if (!_hasPermissions && !_filePermissionRequested)
            {
                StatusLabel.Text = "Status: 🔒 Opening file permission settings...";
                await Task.Delay(500);
                await OpenFilePermissionSettings();
                return; // Exit here, will continue when user returns to app
            }

            // Step 4: Check if default screenshot folder is set, if not show setup dialog
            if (_hasPermissions && _hasOverlayPermission)
            {
                StatusLabel.Text = "Status: 📁 Checking default folder...";
                await Task.Delay(500);
                await CheckAndSetupDefaultFolder();
            }

            // Step 5: Final permission check and UI update
            await CheckPermissions();

            if (_hasPermissions && _hasOverlayPermission)
            {
                StatusLabel.Text = "Status: ✅ All permissions granted - Ready to monitor";
            }
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "Status: ❌ Permission setup failed";
            System.Diagnostics.Debug.WriteLine($"Permission error: {ex}");
        }
    }

    private async Task CheckPermissionsAndContinueFlow()
    {
        try
        {
            Console.WriteLine("🔍 Checking permissions after app resume");
            await CheckPermissions();

            Console.WriteLine($"📊 Current status - Overlay requested: {_overlayPermissionRequested}, Overlay granted: {_hasOverlayPermission}");
            Console.WriteLine($"📊 Current status - File requested: {_filePermissionRequested}, File granted: {_hasPermissions}");

            // Step 1: If we just requested overlay permission and it's now granted, continue to file permissions
            if (_overlayPermissionRequested && _hasOverlayPermission && !_filePermissionRequested)
            {
                StatusLabel.Text = "Status: ✅ Overlay permission granted";
                await Task.Delay(1000);

                // Now request file permissions
                if (!_hasPermissions)
                {
                    StatusLabel.Text = "Status: 🔒 Opening file permission settings...";
                    await Task.Delay(500);
                    await OpenFilePermissionSettings();
                    return; // Exit here, will continue when user returns
                }
            }

            // Step 2: If overlay wasn't requested yet but we need it
            if (!_overlayPermissionRequested && !_hasOverlayPermission)
            {
                StatusLabel.Text = "Status: 🔒 Opening overlay permission settings...";
                await Task.Delay(500);
                await OpenOverlayPermissionSettings();
                return;
            }

            // Step 3: If overlay is granted but file permissions not requested yet
            if (_hasOverlayPermission && !_filePermissionRequested && !_hasPermissions)
            {
                StatusLabel.Text = "Status: 🔒 Opening file permission settings...";
                await Task.Delay(500);
                await OpenFilePermissionSettings();
                return;
            }

            // Step 4: If we just requested file permission and it's now granted
            if (_filePermissionRequested && _hasPermissions)
            {
                StatusLabel.Text = "Status: ✅ File permissions granted";
                await Task.Delay(1000);

                // Now setup default folder if both permissions are granted
                if (_hasOverlayPermission && _hasPermissions)
                {
                    await CheckAndSetupDefaultFolder();
                }
            }

            // Step 5: Final check - if all permissions are granted
            if (_hasPermissions && _hasOverlayPermission)
            {
                StatusLabel.Text = "Status: ✅ All permissions granted - Ready to monitor";
                UpdateUI(); // Make sure UI is updated
            }

            Console.WriteLine($"📊 Final status - Overlay: {_hasOverlayPermission}, Files: {_hasPermissions}");
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "Status: ❌ Permission check failed";
            System.Diagnostics.Debug.WriteLine($"Permission check error: {ex}");
        }
    }

    private async Task CheckPermissions()
    {
        try
        {
            Console.WriteLine("🔍 Starting permission check...");

            // For Android 11+ (API 30+), prioritize MANAGE_EXTERNAL_STORAGE
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.R)
            {
                bool hasManageExternal = Android.OS.Environment.IsExternalStorageManager;
                Console.WriteLine($"📱 Android 11+ - MANAGE_EXTERNAL_STORAGE: {hasManageExternal}");

                if (hasManageExternal)
                {
                    _hasPermissions = true;
                    Console.WriteLine("✅ File permissions granted via MANAGE_EXTERNAL_STORAGE");
                }
                else
                {
                    _hasPermissions = false;
                    Console.WriteLine("❌ MANAGE_EXTERNAL_STORAGE not granted");
                }
            }
            else
            {
                // For Android 10 and below, check Photos/Media permissions
                var photoPermission = await Permissions.CheckStatusAsync<Permissions.Photos>();
                var mediaPermission = await Permissions.CheckStatusAsync<Permissions.Media>();

                _hasPermissions = photoPermission == PermissionStatus.Granted ||
                                 mediaPermission == PermissionStatus.Granted;

                Console.WriteLine($"📱 Android 10 or below:");
                Console.WriteLine($"   📸 Photo permission: {photoPermission}");
                Console.WriteLine($"   🎬 Media permission: {mediaPermission}");
                Console.WriteLine($"   📁 Has file permissions: {_hasPermissions}");
            }

            // Check overlay permission
            _hasOverlayPermission = CheckOverlayPermission();

            Console.WriteLine($"📊 Final permission status:");
            Console.WriteLine($"   📁 File permissions: {_hasPermissions}");
            Console.WriteLine($"   🔲 Overlay permission: {_hasOverlayPermission}");

            // Update UI on main thread
            MainThread.BeginInvokeOnMainThread(() => UpdateUI());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Permission check error: {ex}");
            System.Diagnostics.Debug.WriteLine($"Permission check error: {ex}");
        }
    }

    private bool CheckOverlayPermission()
    {
#if ANDROID
        try
        {
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.M)
            {
                var context = Platform.CurrentActivity ?? Android.App.Application.Context;
                bool hasPermission = Android.Provider.Settings.CanDrawOverlays(context);
                return hasPermission;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error checking overlay permission: {ex.Message}");
        }
#endif
        return true;
    }

    private async Task OpenOverlayPermissionSettings()
    {
#if ANDROID
        try
        {
            _overlayPermissionRequested = true;

            var context = Platform.CurrentActivity ?? Android.App.Application.Context;
            var intent = new Android.Content.Intent(Android.Provider.Settings.ActionManageOverlayPermission);
            intent.SetData(Android.Net.Uri.Parse($"package:{context.PackageName}"));
            Platform.CurrentActivity?.StartActivity(intent);

            System.Diagnostics.Debug.WriteLine("Opened overlay permission settings");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error opening overlay settings: {ex.Message}");
            StatusLabel.Text = "Status: ❌ Failed to open overlay settings";
        }
#endif
    }

    private async Task OpenFilePermissionSettings()
    {
#if ANDROID
        try
        {
            _filePermissionRequested = true;

            var context = Platform.CurrentActivity ?? Android.App.Application.Context;
            Console.WriteLine($"📱 Android SDK: {Android.OS.Build.VERSION.SdkInt}");
            Console.WriteLine($"📁 Current MANAGE_EXTERNAL_STORAGE status: {Android.OS.Environment.IsExternalStorageManager}");

            // For Android 11 and above, MUST use MANAGE_EXTERNAL_STORAGE
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.R)
            {
                if (!Android.OS.Environment.IsExternalStorageManager)
                {
                    Console.WriteLine("🔓 Opening MANAGE_EXTERNAL_STORAGE settings...");

                    var intent = new Android.Content.Intent(Android.Provider.Settings.ActionManageAppAllFilesAccessPermission);
                    intent.SetData(Android.Net.Uri.Parse($"package:{context.PackageName}"));
                    intent.AddFlags(Android.Content.ActivityFlags.NewTask);

                    Platform.CurrentActivity?.StartActivity(intent);
                    System.Diagnostics.Debug.WriteLine("Opened MANAGE_EXTERNAL_STORAGE permission settings");
                    return;
                }
                else
                {
                    Console.WriteLine("✅ MANAGE_EXTERNAL_STORAGE already granted");
                    _hasPermissions = true;
                    return;
                }
            }

            // For Android 10 and below, request photo/media permissions
            Console.WriteLine("📱 Android 10 or below - requesting Photo/Media permissions");

            var photoStatus = await Permissions.RequestAsync<Permissions.Photos>();
            var mediaStatus = await Permissions.RequestAsync<Permissions.Media>();

            Console.WriteLine($"📸 Photo permission result: {photoStatus}");
            Console.WriteLine($"🎬 Media permission result: {mediaStatus}");

            _hasPermissions = photoStatus == PermissionStatus.Granted ||
                             mediaStatus == PermissionStatus.Granted;

            Console.WriteLine($"📊 Final file permissions status: {_hasPermissions}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error in OpenFilePermissionSettings: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Error opening file settings: {ex.Message}");
            StatusLabel.Text = "Status: ❌ Failed to open file settings";
        }
#endif
    }

    private async Task CheckAndSetupDefaultFolder()
    {
        try
        {
            var context = Platform.CurrentActivity ?? Android.App.Application.Context;
            var prefs = context.GetSharedPreferences("screenshot_prefs", Android.Content.FileCreationMode.Private);
            var defaultFolder = prefs?.GetString("default_screenshot_folder", null);

            System.Diagnostics.Debug.WriteLine($"Current default folder: {defaultFolder}");

            if (string.IsNullOrEmpty(defaultFolder))
            {
                await ShowDefaultFolderSetupDialog();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Default folder check error: {ex}");
        }
    }

    private async Task ShowDefaultFolderSetupDialog()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("Showing default folder setup dialog");

            bool setupFolder = await DisplayAlert("Setup Default Screenshot Folder",
                "Please select the folder where your device saves screenshots (usually Pictures/Screenshots)",
                "Select Folder", "Use Default");

            System.Diagnostics.Debug.WriteLine($"User choice for folder setup: {setupFolder}");

            if (setupFolder)
            {
                // This will be handled by MainActivity - we'll send an intent
                await OpenDefaultFolderPicker();
            }
            else
            {
                // Set common default screenshot path
                SetDefaultScreenshotFolder("/storage/emulated/0/Pictures/Screenshots");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Default folder setup error: {ex}");
            // Fallback to default
            SetDefaultScreenshotFolder("/storage/emulated/0/Pictures/Screenshots");
        }
    }

    private async Task OpenDefaultFolderPicker()
    {
#if ANDROID
        try
        {
            System.Diagnostics.Debug.WriteLine("Opening default folder picker");

            var context = Platform.CurrentActivity ?? Android.App.Application.Context;
            var intent = new Android.Content.Intent(context, typeof(MainActivity));
            intent.AddFlags(Android.Content.ActivityFlags.NewTask);
            intent.PutExtra("action", "setup_default_folder");
            context.StartActivity(intent);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Folder picker error: {ex}");
            // Fallback to default
            SetDefaultScreenshotFolder("/storage/emulated/0/Pictures/Screenshots");
        }
#endif
    }

    private void SetDefaultScreenshotFolder(string folderPath)
    {
        try
        {
            var context = Platform.CurrentActivity ?? Android.App.Application.Context;
            var prefs = context.GetSharedPreferences("screenshot_prefs", Android.Content.FileCreationMode.Private);
            prefs?.Edit()?.PutString("default_screenshot_folder", folderPath)?.Apply();

            System.Diagnostics.Debug.WriteLine($"Set default folder to: {folderPath}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error setting default folder: {ex.Message}");
        }
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
            System.Diagnostics.Debug.WriteLine($"Error starting monitoring: {ex}");
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
            System.Diagnostics.Debug.WriteLine($"Error stopping monitoring: {ex}");
        }
    }

    private void UpdateUI()
    {
        var isMonitoring = _monitor.IsMonitoring;
        var hasAllPermissions = _hasPermissions && _hasOverlayPermission;

        StartMonitoringBtn.IsEnabled = hasAllPermissions && !isMonitoring;
        StopMonitoringBtn.IsEnabled = isMonitoring;

        StatusLabel.Text = (hasAllPermissions, isMonitoring) switch
        {
            (false, _) when !_hasOverlayPermission && !_overlayPermissionRequested => "Status: 🔒 Need overlay permission",
            (false, _) when !_hasOverlayPermission && _overlayPermissionRequested => "Status: ⏳ Waiting for overlay permission",
            (false, _) when !_hasPermissions && !_filePermissionRequested => "Status: 🔒 Need file permissions",
            (false, _) when !_hasPermissions && _filePermissionRequested => "Status: ⏳ Waiting for file permissions",
            (true, false) => "Status: ✅ Ready to monitor",
            (true, true) => "Status: 👀 Monitoring active"
        };

        // Hide the permissions button since permissions are requested automatically
        if (PermissionsBtn != null)
        {
            PermissionsBtn.IsVisible = false;
        }

        System.Diagnostics.Debug.WriteLine($"UI Updated - All Permissions: {hasAllPermissions}, Monitoring: {isMonitoring}");
    }
}