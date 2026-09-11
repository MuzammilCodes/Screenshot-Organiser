using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using AndroidX.Core.App;
using AndroidButton = Android.Widget.Button;
using AndroidView = Android.Views.View;
using Color = Android.Graphics.Color;
using ListView = Android.Widget.ListView;
using IOPath = System.IO.Path;


namespace Screenshot_Organiser.Platforms.Android
{
    [Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
    public class OverlayService : Service
    {
        private IWindowManager? _windowManager;
        private AndroidView? _overlayView;
        private AndroidView? _pickerView;
        private static OverlayService? _instance;
        private const int NOTIFICATION_ID = 1001;
        private PowerManager.WakeLock? _wakeLock;
        private bool _isServiceRunning = false;

        public static OverlayService? Instance => _instance;

        public override void OnCreate()
        {
            base.OnCreate();

            try
            {
                _instance = this;
                _isServiceRunning = true;

                _windowManager = GetSystemService(WindowService)?.JavaCast<IWindowManager>();

                // Acquire wake lock to prevent service from being killed
                var powerManager = GetSystemService(PowerService) as PowerManager;
                _wakeLock = powerManager?.NewWakeLock(WakeLockFlags.Partial, "ScreenshotOrganizer::ServiceWakeLock");
                _wakeLock?.Acquire();

                CreateNotificationChannel();

                var notification = CreateNotification();
                StartForeground(NOTIFICATION_ID, notification);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating notification: {ex.Message}");
            }
        }

        public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
        {
            try
            {
                // Handle stop service action
                if (intent?.Action == "STOP_SERVICE")
                {
                    StopSelf();
                    return StartCommandResult.NotSticky;
                }

                // Ensure we're running as foreground service
                if (_isServiceRunning)
                {
                    var notification = CreateNotification();
                    StartForeground(NOTIFICATION_ID, notification);
                }

                return StartCommandResult.Sticky; // Restart if killed
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnStartCommand: {ex.Message}");
                return StartCommandResult.Sticky;
            }
        }

        public override IBinder? OnBind(Intent? intent) => null;

        public void ShowScreenshotDialog(string screenshotPath)
        {
            if (_windowManager == null || !_isServiceRunning) return;

            try
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"Showing overlay for: {screenshotPath}");

                        HideDialog();

                        _overlayView = FolderPickerViewFactory.BuildScreenshotDetectedCard(
                            this,
                            onSelectFolder: () =>
                            {
                                try
                                {
                                    System.Diagnostics.Debug.WriteLine("Select button clicked");
                                    HideDialog();
                                    ShowFolderPickerOverlay(screenshotPath, STORAGE_ROOT);
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Error in select button click: {ex.Message}");
                                }
                            },
                            onCancel: () =>
                            {
                                try
                                {
                                    System.Diagnostics.Debug.WriteLine("Cancel button clicked");
                                    HideDialog();
                                    MarkFileAsProcessed(screenshotPath);
                                    ShowToast("Screenshot kept in original location");
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Error in cancel button click: {ex.Message}");
                                }
                            });

                        int targetWidth = FolderPickerViewFactory.GetPreferredWidth(this);

                        var layoutParams = new WindowManagerLayoutParams(
                            targetWidth,
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
                        System.Diagnostics.Debug.WriteLine($"Error creating overlay view: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing overlay: {ex.Message}");
            }
        }

        private const string STORAGE_ROOT = FolderPickerViewFactory.StorageRoot;

        private void ShowFolderPickerOverlay(string screenshotPath, string currentPath)
        {
            if (_windowManager == null || !_isServiceRunning) return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    HidePicker();

                    var card = FolderPickerViewFactory.BuildFolderPickerCard(
                        this,
                        title: "📂 Move screenshot to…",
                        confirmText: "Move",
                        currentPath: currentPath,
                        onNavigate: path => ShowFolderPickerOverlay(screenshotPath, path),
                        onConfirm: async path =>
                        {
                            HidePicker();
                            await MoveToFolder(screenshotPath, path);
                        },
                        onNewFolder: path => ShowNewFolderOverlay(screenshotPath, path),
                        onCancel: () =>
                        {
                            HidePicker();
                            MarkFileAsProcessed(screenshotPath);
                            ShowToast("Screenshot kept in original location");
                        });

                    AddPickerToWindow(card, focusable: false);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error showing folder picker overlay: {ex.Message}");
                }
            });
        }

        private void ShowNewFolderOverlay(string screenshotPath, string parentPath)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    HidePicker();

                    var card = FolderPickerViewFactory.BuildNewFolderCard(
                        this,
                        parentPath,
                        onCreated: newPath => ShowFolderPickerOverlay(screenshotPath, newPath),
                        onBack: () => ShowFolderPickerOverlay(screenshotPath, parentPath),
                        showMessage: ShowToast);

                    // Needs to be focusable so the keyboard works for the EditText
                    AddPickerToWindow(card, focusable: true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error showing new folder overlay: {ex.Message}");
                }
            });
        }

        private void AddPickerToWindow(AndroidView view, bool focusable)
        {
            if (_windowManager == null) return;

            int targetWidth = FolderPickerViewFactory.GetPreferredWidth(this);

            var flags = focusable
                ? WindowManagerFlags.NotTouchModal
                : WindowManagerFlags.NotFocusable | WindowManagerFlags.NotTouchModal;

            var layoutParams = new WindowManagerLayoutParams(
                targetWidth,
                WindowManagerLayoutParams.WrapContent,
                Build.VERSION.SdkInt >= BuildVersionCodes.O
                    ? WindowManagerTypes.ApplicationOverlay
                    : WindowManagerTypes.Phone,
                flags,
                Format.Translucent)
            {
                Gravity = GravityFlags.Center,
                SoftInputMode = SoftInput.AdjustPan
            };

            _pickerView = view;
            _windowManager.AddView(_pickerView, layoutParams);
        }

        private void HidePicker()
        {
            try
            {
                if (_pickerView != null && _windowManager != null)
                {
                    _windowManager.RemoveView(_pickerView);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error hiding picker: {ex.Message}");
            }
            finally
            {
                _pickerView = null;
            }
        }

        private void ShowBottomSnackbar(string message, int durationMs = 2500)
        {
            if (_windowManager == null) return;

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                AndroidView? bar = null;
                try
                {
                    bar = FolderPickerViewFactory.BuildSnackbarCard(this, message);

                    var density = Resources?.DisplayMetrics?.Density ?? 1f;
                    var layoutParams = new WindowManagerLayoutParams(
                        WindowManagerLayoutParams.WrapContent,
                        WindowManagerLayoutParams.WrapContent,
                        Build.VERSION.SdkInt >= BuildVersionCodes.O
                            ? WindowManagerTypes.ApplicationOverlay
                            : WindowManagerTypes.Phone,
                        WindowManagerFlags.NotFocusable | WindowManagerFlags.NotTouchable,
                        Format.Translucent)
                    {
                        Gravity = GravityFlags.Bottom | GravityFlags.CenterHorizontal,
                        Y = (int)(72 * density)
                    };

                    _windowManager.AddView(bar, layoutParams);

                    await Task.Delay(durationMs);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error showing snackbar: {ex.Message}");
                }
                finally
                {
                    try
                    {
                        if (bar != null) _windowManager?.RemoveView(bar);
                    }
                    catch { /* view may already be gone */ }
                }
            });
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

                int counter = 1;
                while (File.Exists(destinationPath))
                {
                    var nameWithoutExt = IOPath.GetFileNameWithoutExtension(fileName);
                    var extension = IOPath.GetExtension(fileName);
                    destinationPath = IOPath.Combine(destinationFolder, $"{nameWithoutExt}_{counter}{extension}");
                    counter++;
                }

                ModernScreenshotMonitor.MarkFileAsMoved(screenshotPath);

                File.Copy(screenshotPath, destinationPath, overwrite: true);
                File.Delete(screenshotPath);

                var folderName = IOPath.GetFileName(destinationFolder);
                ShowBottomSnackbar($"Moved to {folderName} ✅");

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
            try
            {
                if (_overlayView != null && _windowManager != null)
                {
                    _windowManager.RemoveView(_overlayView);
                    System.Diagnostics.Debug.WriteLine("Overlay dialog hidden");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error hiding overlay: {ex.Message}");
            }
            finally
            {
                _overlayView = null;
            }
        }

        private void ShowToast(string message)
        {
            try
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        Toast.MakeText(this, message, ToastLength.Long)?.Show();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error showing toast: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ShowToast: {ex.Message}");
            }
        }

        private void CreateNotificationChannel()
        {
            try
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                {
                    var channel = new NotificationChannel(
                        "screenshot_service",
                        "Screenshot Organizer",
                        NotificationImportance.Low)
                    {
                        Description = "Monitors for new screenshots and organizes them automatically"
                    };

                    channel.SetShowBadge(false);
                    channel.EnableLights(false);
                    channel.EnableVibration(false);
                    channel.SetSound(null, null);

                    var notificationManager = GetSystemService(NotificationService) as NotificationManager;
                    notificationManager?.CreateNotificationChannel(channel);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating notification channel: {ex.Message}");
            }
        }

        private Notification CreateNotification()
        {
            try
            {
                var intent = new Intent(this, typeof(MainActivity));
                intent.AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
                var pendingIntent = PendingIntent.GetActivity(this, 0, intent,
                    Build.VERSION.SdkInt >= BuildVersionCodes.M ? PendingIntentFlags.Immutable : PendingIntentFlags.UpdateCurrent);

                var stopIntent = new Intent(this, typeof(OverlayService));
                stopIntent.SetAction("STOP_SERVICE");
                var stopPendingIntent = PendingIntent.GetService(this, 1, stopIntent,
                    Build.VERSION.SdkInt >= BuildVersionCodes.M ? PendingIntentFlags.Immutable : PendingIntentFlags.UpdateCurrent);

                return new NotificationCompat.Builder(this, "screenshot_service")
                    .SetContentTitle("Screenshot Organizer Active")
                    .SetContentText("Monitoring screenshots in background")
                    .SetContentIntent(pendingIntent)
                    .SetOngoing(true)
                    .SetPriority(NotificationCompat.PriorityLow)
                    .SetAutoCancel(false)
                    .SetCategory(NotificationCompat.CategoryService)
                    .AddAction(global::Android.Resource.Drawable.IcMenuCloseClearCancel, "Stop", stopPendingIntent)
                    .SetSmallIcon(global::Android.Resource.Drawable.IcMenuCamera)
                    .Build();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating notification: {ex.Message}");
                // Return a basic notification as fallback
                return new NotificationCompat.Builder(this, "screenshot_service")
                    .SetContentTitle("Screenshot Organizer")
                    .SetContentText("Running...")
                    .SetSmallIcon(global::Android.Resource.Drawable.IcMenuCamera)
                    .Build();
            }
        }

        public override void OnDestroy()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("OverlayService OnDestroy called");

                _isServiceRunning = false;
                HideDialog();
                HidePicker();

                _wakeLock?.Release();
                _wakeLock = null;

                _instance = null;
                StopForeground(true);

                base.OnDestroy();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnDestroy: {ex.Message}");
                base.OnDestroy();
            }
        }

        public override void OnTaskRemoved(Intent? rootIntent)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("OverlayService OnTaskRemoved - App removed from recent apps");
                base.OnTaskRemoved(rootIntent);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnTaskRemoved: {ex.Message}");
                base.OnTaskRemoved(rootIntent);
            }
        }

        public override void OnLowMemory()
        {
            System.Diagnostics.Debug.WriteLine("OverlayService OnLowMemory called");
            base.OnLowMemory();
        }
    }
}