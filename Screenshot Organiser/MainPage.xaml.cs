using Android.Content;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Screenshot_Organiser;

public partial class MainPage : ContentPage, INotifyPropertyChanged
{
    private readonly ModernScreenshotMonitor _monitor;
    private const string FolderSetupInProgressKey = "folder_setup_in_progress";

    private const int InitialDelayMs = 600;
    private const int AfterPermissionGrantedDelayMs = 800;

    private bool _hasPermissions;
    private bool _hasOverlayPermission;
    private bool _permissionsRequested;
    private bool _overlayPermissionRequested;
    private bool _filePermissionRequested;
    private bool _initialLoadComplete;
    private bool _defaultFolderSetupComplete;
    private bool _monitorToggle;


    public MainPage()
    {
        InitializeComponent();

        _monitor = new ModernScreenshotMonitor();
        BindingContext = this;

        ApplyIconTints();

        if (Application.Current != null)
            Application.Current.RequestedThemeChanged += (_, _) =>
                MainThread.BeginInvokeOnMainThread(ApplyIconTints);
    }

    private void ApplyIconTints()
    {
        var isDark = (Application.Current?.RequestedTheme ?? AppTheme.Light) == AppTheme.Dark;
        var key = isDark ? "TextPrimaryDark" : "TextPrimaryLight";

        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color tint)
        {
            OverlayIconTint.TintColor = tint;
            FilesIconTint.TintColor = tint;
            DefaultFolderIconTint.TintColor = tint;
        }
    }


    #region Bindable Properties (UI)

    public bool HasOverlayPermission
    {
        get => _hasOverlayPermission;
        set
        {
            if (_hasOverlayPermission != value)
            {
                _hasOverlayPermission = value;
                OnPropertyChanged();
            }
        }
    }

    public bool HasFilePermission
    {
        get => _hasPermissions;
        set
        {
            if (_hasPermissions != value)
            {
                _hasPermissions = value;
                OnPropertyChanged();
            }
        }
    }

    public bool CanStartMonitoring =>
        HasOverlayPermission && HasFilePermission && _defaultFolderSetupComplete && !_monitor.IsMonitoring;

    public bool HasDefaultFolder =>
    _defaultFolderSetupComplete;

    public bool IsMonitoring => _monitor?.IsMonitoring ?? false;

    public bool CanToggleMonitoring =>
    HasOverlayPermission &&
    HasFilePermission &&
    _defaultFolderSetupComplete;

    public bool MonitorToggle
    {
        get => _monitorToggle;
        set
        {
            if (_monitorToggle == value)
                return;

            _monitorToggle = value;
            OnPropertyChanged();

            // 🔁 React to toggle change
            _ = HandleMonitoringToggleAsync(value);
        }
    }


    #endregion

    #region Commands (Card Taps)

    public ICommand OpenOverlayPermissionCommand =>
        new Command(async () =>
        {
            if (HasOverlayPermission)
                return;

            await OpenOverlayPermissionSettings();
        });


    public ICommand OpenFilePermissionCommand =>
        new Command(async () =>
        {
            if (HasFilePermission)
                return;

            await OpenFilePermissionSettings();
        });

    public ICommand OpenDefaultFolderCommand =>
        new Command(async () =>
        {
            if (_defaultFolderSetupComplete)
                return;

            await CheckAndSetupDefaultFolder();
        });

    #endregion

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_initialLoadComplete)
            return;

        _initialLoadComplete = true;

        try
        {
            await Task.Delay(InitialDelayMs);

            if (!_permissionsRequested)
            {
                _permissionsRequested = true;
                await RequestPermissionsSequentially();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error during initial load: {ex.Message}");
        }
    }

    public async void OnAppResumed()
    {
        try
        {
            if (_permissionsRequested)
                await CheckPermissionsAndContinueFlow();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error on app resume: {ex.Message}");
        }
    }

    #region Permission Flow

    private async Task RequestPermissionsSequentially()
    {
        await CheckPermissions();

        if (!HasOverlayPermission && !_overlayPermissionRequested)
        {
            await Task.Delay(InitialDelayMs);
            await OpenOverlayPermissionSettings();
            return;
        }

        if (!HasFilePermission && !_filePermissionRequested)
        {
            await OpenFilePermissionSettings();
            return;
        }

        if (HasOverlayPermission && HasFilePermission && !_defaultFolderSetupComplete)
        {
            await CheckAndSetupDefaultFolder();
        }

    }

    private async Task CheckPermissionsAndContinueFlow()
    {
        await CheckPermissions();

        if (_overlayPermissionRequested && HasOverlayPermission && !_filePermissionRequested)
        {
            if (!HasFilePermission)
            {
                await Task.Delay(AfterPermissionGrantedDelayMs);

                await OpenFilePermissionSettings();
                return;
            }
        }

        if (HasOverlayPermission && HasFilePermission && !_defaultFolderSetupComplete)
        {
            await Task.Delay(AfterPermissionGrantedDelayMs);
            await CheckAndSetupDefaultFolder();
        }

    }

    private async Task CheckPermissions()
    {
        if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.R)
        {
            HasFilePermission = Android.OS.Environment.IsExternalStorageManager;
        }
        else
        {
            var photo = await Permissions.CheckStatusAsync<Permissions.Photos>();
            var media = await Permissions.CheckStatusAsync<Permissions.Media>();

            HasFilePermission =
                photo == PermissionStatus.Granted ||
                media == PermissionStatus.Granted;
        }

        HasOverlayPermission = CheckOverlayPermission();

        UpdateComputedStates();
        MainThread.BeginInvokeOnMainThread(UpdateComputedStates);
    }

    private bool CheckOverlayPermission()
    {
#if ANDROID
        var context = Platform.CurrentActivity ?? Android.App.Application.Context;
        return Android.Provider.Settings.CanDrawOverlays(context);
#else
        return true;
#endif
    }

    #endregion

    #region Open Permission Screens

    private async Task OpenOverlayPermissionSettings()
    {
#if ANDROID
        _overlayPermissionRequested = true;

        var context = Platform.CurrentActivity ?? Android.App.Application.Context;

        var intent = new Intent(
            Android.Provider.Settings.ActionManageOverlayPermission);

        intent.SetData(Android.Net.Uri.Parse($"package:{context.PackageName}"));
        intent.AddFlags(ActivityFlags.NewTask);
        intent.AddFlags(ActivityFlags.NoHistory);
        intent.AddFlags(ActivityFlags.ExcludeFromRecents);

        Platform.CurrentActivity?.StartActivity(intent);
#endif
        await Task.CompletedTask;
    }

    private async Task OpenFilePermissionSettings()
    {
#if ANDROID
        _filePermissionRequested = true;

        var context = Platform.CurrentActivity ?? Android.App.Application.Context;

        if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.R)
        {
            if (!Android.OS.Environment.IsExternalStorageManager)
            {
                var intent = new Intent(
                    Android.Provider.Settings.ActionManageAppAllFilesAccessPermission);

                intent.SetData(Android.Net.Uri.Parse($"package:{context.PackageName}"));
                intent.AddFlags(ActivityFlags.NewTask);
                intent.AddFlags(ActivityFlags.NoHistory);
                intent.AddFlags(ActivityFlags.ExcludeFromRecents);

                Platform.CurrentActivity?.StartActivity(intent);
            }
            else
            {
                HasFilePermission = true;
            }
        }
        else
        {
            var photo = await Permissions.RequestAsync<Permissions.Photos>();
            var media = await Permissions.RequestAsync<Permissions.Media>();

            HasFilePermission =
                photo == PermissionStatus.Granted ||
                media == PermissionStatus.Granted;
        }
#endif
        await Task.CompletedTask;
    }

    #endregion

    #region Default Folder

    private async Task CheckAndSetupDefaultFolder()
    {
        if (_defaultFolderSetupComplete)
            return;

        var context = Platform.CurrentActivity ?? Android.App.Application.Context;
        var prefs = context.GetSharedPreferences("screenshot_prefs", FileCreationMode.Private);

        // 🔒 CRITICAL: prevent double popup
        var folderSetupInProgress =
            prefs?.GetBoolean(FolderSetupInProgressKey, false) ?? false;

        if (folderSetupInProgress)
        {
            System.Diagnostics.Debug.WriteLine("📁 Folder setup already in progress, skipping...");
            return;
        }

        var folder = prefs?.GetString("default_screenshot_folder", null);

        if (!string.IsNullOrEmpty(folder))
        {
            _defaultFolderSetupComplete = true;
            return;
        }

        // 🔒 Lock before showing dialog
        prefs?.Edit()?.PutBoolean(FolderSetupInProgressKey, true)?.Apply();

        await ShowDefaultFolderSetupDialog();

    }

    private async Task ShowDefaultFolderSetupDialog()
    {
        var context = Platform.CurrentActivity ?? Android.App.Application.Context;
        var prefs = context.GetSharedPreferences("screenshot_prefs", FileCreationMode.Private);

        try
        {
            bool choose = await ShowStyledConfirmationDialog(
                "📂 Default Screenshot Folder",
                "Select where screenshots are stored",
                "Select Folder");

            if (choose)
            {
                await OpenDefaultFolderPicker();
            }
            else
            {
                prefs?.Edit()?.PutBoolean(FolderSetupInProgressKey, false)?.Apply();
            }
        }
        catch
        {
            // 🔓 Clear lock on error
            prefs?.Edit()?.PutBoolean(FolderSetupInProgressKey, false)?.Apply();
            throw;
        }
    }

    private Task<bool> ShowStyledConfirmationDialog(string title, string message, string confirmText)
    {
        var tcs = new TaskCompletionSource<bool>();

#if ANDROID
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                var activity = Platform.CurrentActivity;
                if (activity == null)
                {
                    tcs.TrySetResult(false);
                    return;
                }

                Android.App.Dialog? dialog = null;

                var card = Screenshot_Organiser.Platforms.Android.FolderPickerViewFactory.BuildConfirmationCard(
                    activity,
                    title,
                    message,
                    confirmText,
                    onConfirm: () =>
                    {
                        dialog?.Dismiss();
                        tcs.TrySetResult(true);
                    },
                    onCancel: () =>
                    {
                        dialog?.Dismiss();
                        tcs.TrySetResult(false);
                    });

                dialog = new Android.App.Dialog(activity);
                dialog.RequestWindowFeature((int)Android.Views.WindowFeatures.NoTitle);
                dialog.SetContentView(card);
                dialog.SetCancelable(false);

                var window = dialog.Window;
                window?.SetBackgroundDrawable(new Android.Graphics.Drawables.ColorDrawable(Android.Graphics.Color.Transparent));
                window?.SetLayout(
                    Screenshot_Organiser.Platforms.Android.FolderPickerViewFactory.GetPreferredWidth(activity),
                    Android.Views.ViewGroup.LayoutParams.WrapContent);

                dialog.Show();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing styled confirmation: {ex.Message}");
                tcs.TrySetResult(false);
            }
        });
#else
        tcs.TrySetResult(false);
#endif

        return tcs.Task;
    }



    private async Task OpenDefaultFolderPicker()
    {
#if ANDROID
        var context = Platform.CurrentActivity ?? Android.App.Application.Context;

        var intent = new Intent(context, typeof(MainActivity));
        intent.AddFlags(ActivityFlags.NewTask);
        intent.PutExtra("action", "setup_default_folder");

        context.StartActivity(intent);
#endif
        await Task.CompletedTask;
    }

    public void OnDefaultFolderSet()
    {
        _defaultFolderSetupComplete = true;

        var context = Platform.CurrentActivity ?? Android.App.Application.Context;
        var prefs = context.GetSharedPreferences("screenshot_prefs", FileCreationMode.Private);

        // 🔓 CRITICAL
        prefs?.Edit()?.PutBoolean(FolderSetupInProgressKey, false)?.Apply();

        MainThread.BeginInvokeOnMainThread(UpdateComputedStates);
    }

    #endregion

    #region Monitoring

    private async Task HandleMonitoringToggleAsync(bool enabled)
    {
        // Safety: only allow ON if everything is ready
        if (enabled)
        {
            if (!CanStartMonitoring)
            {
                // Revert toggle if user tries early
                _monitorToggle = false;
                OnPropertyChanged(nameof(MonitorToggle));
                return;
            }

            await _monitor.StartMonitoring();
        }
        else
        {
            await _monitor.StopMonitoring();
        }

        MainThread.BeginInvokeOnMainThread(UpdateComputedStates);
    }

    #endregion  

    #region Helpers

    private void UpdateComputedStates()
    {
        OnPropertyChanged(nameof(CanStartMonitoring));
        OnPropertyChanged(nameof(CanToggleMonitoring));
        OnPropertyChanged(nameof(IsMonitoring));
        OnPropertyChanged(nameof(HasDefaultFolder));
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    #endregion
}
