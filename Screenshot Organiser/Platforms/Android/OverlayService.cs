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

            // Pre-create notification to ensure it's ready
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
            // Don't start as foreground service - run as regular service
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
                // Simple folder selection with predefined options
                var items = new[]
                {
            "📁 Browse Folders",
            "📷 Pictures",
            "📱 Downloads",
            "📄 Documents",
            "🎵 Music",
            "🎬 Movies"
        };

                var builder = new AlertDialog.Builder(this);
                builder.SetTitle("Select Folder");
                builder.SetItems(items, async (sender, args) =>
                {
                    switch (args.Which)
                    {
                        case 0: // Browse Folders
                            await OpenSystemFilePicker(screenshotPath);
                            break;
                        case 1: // Pictures
                            await MoveToFolder(screenshotPath, "/storage/emulated/0/Pictures");
                            break;
                        case 2: // Downloads
                            await MoveToFolder(screenshotPath, "/storage/emulated/0/Download");
                            break;
                        case 3: // Documents
                            await MoveToFolder(screenshotPath, "/storage/emulated/0/Documents");
                            break;
                        case 4: // Music
                            await MoveToFolder(screenshotPath, "/storage/emulated/0/Music");
                            break;
                        case 5: // Movies
                            await MoveToFolder(screenshotPath, "/storage/emulated/0/Movies");
                            break;
                    }
                });

                var dialog = builder.Create();

                // Make it overlay
                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                {
                    dialog.Window?.SetType(WindowManagerTypes.ApplicationOverlay);
                }

                dialog.Show();
            }
            catch (Exception ex)
            {
                ShowToast($"Error: {ex.Message}");
            }
        }



        private async Task OpenSystemFilePicker(string screenshotPath)
        {
            try
            {
                // Store screenshot path
                var prefs = GetSharedPreferences("screenshot_prefs", FileCreationMode.Private);
                prefs?.Edit()?.PutString("pending_screenshot", screenshotPath)?.Apply();

                // Start MainActivity to handle folder picker
                var intent = new Intent(this, typeof(MainActivity));
                intent.AddFlags(ActivityFlags.NewTask);
                intent.PutExtra("action", "pick_folder");

                StartActivity(intent);
                ShowToast("📂 Select folder in the app");
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
                // Wait a moment for file to be ready
                await Task.Delay(500);

                if (!File.Exists(screenshotPath))
                {
                    ShowToast("❌ Screenshot file not found");
                    return;
                }

                // Create destination folder
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

                // Move the file
                File.Move(screenshotPath, destinationPath);

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