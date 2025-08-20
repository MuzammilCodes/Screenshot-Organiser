using Android.App;
using Android.Content;
using Android.Graphics;
using AndroidOS = Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.Core.App;
using AndroidButton = Android.Widget.Button;
using AndroidView = Android.Views.View;
using AndroidTextView = Android.Widget.TextView;
using AndroidLinearLayout = Android.Widget.LinearLayout;
using AndroidEnvironment = Android.OS.Environment;
using IOPath = System.IO.Path;
using Android.Runtime;



using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.Core.App;
using Android.Content.PM;

namespace Screenshot_Organiser.Platforms.Android
{
    [Service(Exported = false)]
    public class OverlayService : Service
    {
        private IWindowManager? _windowManager;
        private AndroidView? _overlayView;
        private static OverlayService? _instance;
        private const int NOTIFICATION_ID = 1001;

        public static OverlayService? Instance => _instance;

        public override void OnCreate()
        {
            base.OnCreate();
            _instance = this;
            _windowManager = GetSystemService(WindowService)?.JavaCast<IWindowManager>();
            CreateNotificationChannel();
        }

        public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
        {
            try
            {
                if (AndroidOS.Build.VERSION.SdkInt >= AndroidOS.BuildVersionCodes.Q)
                {
                    StartForeground(NOTIFICATION_ID, CreateNotification(), ForegroundService.TypeSpecialUse);
                }
                else
                {
                    StartForeground(NOTIFICATION_ID, CreateNotification());
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting foreground service: {ex.Message}");
                // Fallback: try without foreground service
                return StartCommandResult.Sticky;
            }

            return StartCommandResult.Sticky;
        }

        public override IBinder? OnBind(Intent? intent) => null;

        public void ShowScreenshotDialog(string screenshotPath)
        {
            if (_windowManager == null) return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    // Remove existing overlay if present
                    HideDialog();

                    // Create overlay layout
                    _overlayView = LayoutInflater.From(this)?.Inflate(Resource.Layout.overlay_screenshot_dialog, null);

                    if (_overlayView == null) return;

                    // Setup button click handlers
                    var selectBtn = _overlayView.FindViewById<AndroidButton>(Resource.Id.btnSelect);
                    var cancelBtn = _overlayView.FindViewById<AndroidButton>(Resource.Id.btnCancel);

                    selectBtn!.Click += async (s, e) =>
                    {
                        HideDialog();
                        await HandleSelectFolder(screenshotPath);
                    };

                    cancelBtn!.Click += (s, e) =>
                    {
                        HideDialog();
                        ShowToast("Screenshot saved in original location");
                    };

                    // Setup overlay parameters
                    var layoutParams = new WindowManagerLayoutParams(
                        WindowManagerLayoutParams.MatchParent,
                        WindowManagerLayoutParams.WrapContent,
                        Build.VERSION.SdkInt >= BuildVersionCodes.O
                            ? WindowManagerTypes.ApplicationOverlay
                            : WindowManagerTypes.Phone,
                        WindowManagerFlags.NotFocusable | WindowManagerFlags.NotTouchModal,
                        Format.Translucent)
                    {
                        Gravity = GravityFlags.Center
                    };

                    _windowManager.AddView(_overlayView, layoutParams);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error showing overlay: {ex.Message}");
                }
            });
        }

        private async Task HandleSelectFolder(string screenshotPath)
        {
            try
            {
                // Create folder selection dialog
                var folderDialog = new AlertDialog.Builder(this)
                    .SetTitle("Select Folder")
                    .SetMessage("Choose where to save the screenshot")
                    .SetPositiveButton("Browse Folders", async (s, e) =>
                    {
                        await OpenFolderPicker(screenshotPath);
                    })
                    .SetNeutralButton("Pictures Folder", async (s, e) =>
                    {
                        var picturesPath = IOPath.Combine(
                            AndroidEnvironment.ExternalStorageDirectory?.AbsolutePath ?? "/storage/emulated/0",
                            "Pictures");
                        await SaveScreenshot(screenshotPath, picturesPath);
                    })
                    .SetNegativeButton("Cancel", (s, e) =>
                    {
                        ShowToast("Screenshot remains in original location");
                    })
                    .Create();

                // Make dialog appear as overlay
                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                {
                    folderDialog.Window?.SetType(WindowManagerTypes.ApplicationOverlay);
                }
                else
                {
                    folderDialog.Window?.SetType(WindowManagerTypes.SystemAlert);
                }

                folderDialog.Show();
            }
            catch (Exception ex)
            {
                ShowToast($"Error: {ex.Message}");
            }
        }

        private async Task OpenFolderPicker(string screenshotPath)
        {
            try
            {
                var intent = new Intent(Intent.ActionOpenDocumentTree);
                intent.AddFlags(ActivityFlags.NewTask);

                // Store screenshot path for later use
                var prefs = GetSharedPreferences("screenshot_prefs", FileCreationMode.Private);
                prefs?.Edit()?.PutString("pending_screenshot", screenshotPath)?.Apply();

                StartActivity(intent);
            }
            catch (Exception ex)
            {
                ShowToast($"Failed to open folder picker: {ex.Message}");
            }
        }

        private async Task SaveScreenshot(string sourcePath, string destinationFolder)
        {
            try
            {
                Directory.CreateDirectory(destinationFolder);

                var fileName = IOPath.GetFileName(sourcePath);
                var destinationPath = IOPath.Combine(destinationFolder, fileName);

                // Wait for file to be fully written
                await Task.Delay(1000);

                File.Copy(sourcePath, destinationPath, true);
                File.Delete(sourcePath);

                ShowToast($"Screenshot saved to {IOPath.GetFileName(destinationFolder)}");
            }
            catch (Exception ex)
            {
                ShowToast($"Failed to save: {ex.Message}");
            }
        }

        public void HideDialog()
        {
            if (_overlayView != null && _windowManager != null)
            {
                try
                {
                    _windowManager.RemoveView(_overlayView);
                }
                catch (Exception)
                {
                    // View might not be attached
                }
                _overlayView = null;
            }
        }

        private void ShowToast(string message)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Toast.MakeText(this, message, ToastLength.Long)?.Show();
            });
        }

        private void CreateNotificationChannel()
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                var channel = new NotificationChannel(
                    "screenshot_service",
                    "Screenshot Monitor",
                    NotificationImportance.Low)
                {
                    Description = "Monitors for new screenshots"
                };

                var notificationManager = GetSystemService(NotificationService) as NotificationManager;
                notificationManager?.CreateNotificationChannel(channel);
            }
        }

        private Notification CreateNotification()
        {
            var intent = new Intent(this, typeof(MainActivity));
            var pendingIntent = PendingIntent.GetActivity(this, 0, intent, PendingIntentFlags.Immutable);

            return new NotificationCompat.Builder(this, "screenshot_service")
                .SetContentTitle("Screenshot Monitor")
                .SetContentText("Monitoring for screenshots...")
                .SetSmallIcon(Resource.Drawable.ic_screenshot)
                .SetContentIntent(pendingIntent)
                .SetOngoing(true)
                .SetPriority(NotificationCompat.PriorityLow)
                .Build();
        }

        public override void OnDestroy()
        {
            HideDialog();
            _instance = null;
            base.OnDestroy();
        }
    }
}