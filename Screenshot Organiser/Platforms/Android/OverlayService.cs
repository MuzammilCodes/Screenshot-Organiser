using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using AndroidX.Core.App;
using AndroidButton = Android.Widget.Button;
using AndroidView = Android.Views.View;
using IOPath = System.IO.Path;

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

            try
            {
                var notification = CreateNotification();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating notification: {ex.Message}");
            }
        }

        public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
        {
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
                    HideDialog();

                    _overlayView = LayoutInflater.From(this)?.Inflate(Resource.Layout.overlay_screenshot_dialog, null);
                    if (_overlayView == null) return;

                    string fileName = IOPath.GetFileName(screenshotPath);
                    var txtPath = _overlayView.FindViewById<TextView>(Resource.Id.txtScreenshotPath);
                    if (txtPath != null)
                    {
                        txtPath.Text = fileName;
                    }

                    // Setup button click handlers
                    var selectBtn = _overlayView.FindViewById<AndroidButton>(Resource.Id.btnSelect);
                    var cancelBtn = _overlayView.FindViewById<AndroidButton>(Resource.Id.btnCancel);

                    selectBtn!.Click += async (s, e) =>
                    {
                        HideDialog();
                        await OpenSystemFilePicker(screenshotPath);
                    };

                    cancelBtn!.Click += (s, e) =>
                    {
                        HideDialog();
                        // Mark the original file as "processed" to avoid re-detection
                        MarkFileAsProcessed(screenshotPath);
                        ShowToast("Screenshot kept in original location");
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

        private async Task OpenSystemFilePicker(string screenshotPath)
        {
            try
            {
                var prefs = GetSharedPreferences("screenshot_prefs", FileCreationMode.Private);
                prefs?.Edit()?.PutString("pending_screenshot", screenshotPath)?.Apply();

                var intent = new Intent(this, typeof(MainActivity));
                intent.AddFlags(ActivityFlags.NewTask);
                intent.PutExtra("action", "pick_folder");

                StartActivity(intent);
            }
            catch (Exception ex)
            {
                await MoveToFolder(screenshotPath, "/storage/emulated/0/Download");
                ShowToast("📥 Moved to Downloads folder");
            }
        }

        private async Task MoveToFolder(string screenshotPath, string destinationFolder)
        {
            try
            {
                await Task.Delay(500);

                if (!File.Exists(screenshotPath))
                {
                    ShowToast("❌ Screenshot file not found");
                    return;
                }

                Directory.CreateDirectory(destinationFolder);

                var fileName = IOPath.GetFileName(screenshotPath);
                var destinationPath = IOPath.Combine(destinationFolder, fileName);

                // Handle duplicate names
                int counter = 1;
                while (File.Exists(destinationPath))
                {
                    var nameWithoutExt = IOPath.GetFileNameWithoutExtension(fileName);
                    var extension = IOPath.GetExtension(fileName);
                    destinationPath = IOPath.Combine(destinationFolder, $"{nameWithoutExt}_{counter}{extension}");
                    counter++;
                }

                // IMPORTANT: Mark the original file as moved BEFORE moving it
                ModernScreenshotMonitor.MarkFileAsMoved(screenshotPath);

                // Move the file
                File.Copy(screenshotPath, destinationPath, overwrite: true);
                File.Delete(screenshotPath);

                var folderName = IOPath.GetFileName(destinationFolder);
                ShowToast($"✅ Screenshot moved to {folderName}");

                System.Diagnostics.Debug.WriteLine($"✅ Screenshot moved: {screenshotPath} → {destinationPath}");
            }
            catch (Exception ex)
            {
                ShowToast($"❌ Failed to move: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ Move error: {ex.Message}");
            }
        }

        private void MarkFileAsProcessed(string filePath)
        {
            try
            {
                var prefs = GetSharedPreferences("screenshot_prefs", FileCreationMode.Private);
                var editor = prefs?.Edit();
                editor?.PutLong($"processed_{filePath.GetHashCode()}", DateTimeOffset.Now.ToUnixTimeMilliseconds());
                editor?.Apply();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error marking file as processed: {ex.Message}");
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