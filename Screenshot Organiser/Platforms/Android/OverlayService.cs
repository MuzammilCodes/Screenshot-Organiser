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

                        _overlayView = LayoutInflater.From(this)?.Inflate(Screenshot_Organiser.Resource.Layout.overlay_screenshot_dialog, null);
                        if (_overlayView == null) return;

                        // Setup button click handlers
                        var selectBtn = _overlayView.FindViewById<AndroidButton>(Screenshot_Organiser.Resource.Id.btnSelect);
                        var cancelBtn = _overlayView.FindViewById<AndroidButton>(Screenshot_Organiser.Resource.Id.btnCancel);

                        selectBtn!.Click += (s, e) =>
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
                        };

                        cancelBtn!.Click += (s, e) =>
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
                        };

                        int screenWidth = Resources?.DisplayMetrics?.WidthPixels ?? 1080;
                        var density = Resources?.DisplayMetrics?.Density ?? 1f;
                        var screenWidthDp = screenWidth / density;

                        int targetWidth = screenWidthDp >= 600
                            ? (int)(screenWidth * 0.45f)
                            : (int)(screenWidth * 0.8f);

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

        private const string STORAGE_ROOT = "/storage/emulated/0";

        private void ShowFolderPickerOverlay(string screenshotPath, string currentPath)
        {
            if (_windowManager == null || !_isServiceRunning) return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    HidePicker();

                    var density = Resources?.DisplayMetrics?.Density ?? 1f;
                    int Dp(int dp) => (int)(dp * density);

                    // Root card
                    var card = new LinearLayout(this)
                    {
                        Orientation = Orientation.Vertical
                    };
                    var cardBg = new global::Android.Graphics.Drawables.GradientDrawable();
                    cardBg.SetColor(Color.ParseColor("#FFFFFF"));
                    cardBg.SetCornerRadius(Dp(20));
                    card.Background = cardBg;
                    card.SetPadding(Dp(20), Dp(20), Dp(20), Dp(12));
                    card.Elevation = Dp(8);

                    // Title
                    var title = new TextView(this)
                    {
                        Text = "📂 Move screenshot to…"
                    };
                    title.SetTextColor(Color.ParseColor("#1A1A1A"));
                    title.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 18);
                    title.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
                    card.AddView(title);

                    // Current path subtitle
                    var pathLabel = new TextView(this)
                    {
                        Text = currentPath.Replace(STORAGE_ROOT, "Internal storage")
                    };
                    pathLabel.SetTextColor(Color.ParseColor("#757575"));
                    pathLabel.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 13);
                    pathLabel.SetPadding(0, Dp(4), 0, Dp(10));
                    pathLabel.SetSingleLine(true);
                    pathLabel.Ellipsize = global::Android.Text.TextUtils.TruncateAt.Start;
                    card.AddView(pathLabel);

                    // Folder list
                    string[] subFolders;
                    try
                    {
                        subFolders = Directory.GetDirectories(currentPath)
                            .Where(d => !IOPath.GetFileName(d).StartsWith("."))
                            .OrderBy(d => IOPath.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                            .ToArray();
                    }
                    catch
                    {
                        subFolders = Array.Empty<string>();
                    }

                    bool canGoUp = !string.Equals(currentPath.TrimEnd('/'), STORAGE_ROOT, StringComparison.OrdinalIgnoreCase);

                    var items = new List<string>();
                    if (canGoUp) items.Add("⬆️  ..");
                    items.AddRange(subFolders.Select(d => "📁  " + IOPath.GetFileName(d)));

                    var listView = new ListView(this)
                    {
                        Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleListItem1, items),
                        Divider = null
                    };
                    listView.ItemClick += (s, e) =>
                    {
                        try
                        {
                            if (canGoUp && e.Position == 0)
                            {
                                var parent = IOPath.GetDirectoryName(currentPath.TrimEnd('/')) ?? STORAGE_ROOT;
                                ShowFolderPickerOverlay(screenshotPath, parent);
                            }
                            else
                            {
                                var index = canGoUp ? e.Position - 1 : e.Position;
                                ShowFolderPickerOverlay(screenshotPath, subFolders[index]);
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error navigating folder: {ex.Message}");
                        }
                    };

                    int screenHeight = Resources?.DisplayMetrics?.HeightPixels ?? 1920;
                    var listParams = new LinearLayout.LayoutParams(
                        ViewGroup.LayoutParams.MatchParent, (int)(screenHeight * 0.35f));
                    card.AddView(listView, listParams);

                    // Buttons row
                    var buttonRow = new LinearLayout(this)
                    {
                        Orientation = Orientation.Horizontal
                    };
                    buttonRow.SetPadding(0, Dp(10), 0, 0);

                    AndroidButton MakeFlatButton(string text, Color textColor)
                    {
                        var btn = new AndroidButton(this) { Text = text };
                        btn.SetTextColor(textColor);
                        btn.SetBackgroundColor(Color.Transparent);
                        btn.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 14);
                        btn.SetAllCaps(false);
                        return btn;
                    }

                    var cancelBtn = MakeFlatButton("Cancel", Color.ParseColor("#757575"));
                    var newFolderBtn = MakeFlatButton("New folder", Color.ParseColor("#1976D2"));
                    var moveHereBtn = new AndroidButton(this) { Text = "Move here" };
                    moveHereBtn.SetTextColor(Color.White);
                    moveHereBtn.SetAllCaps(false);
                    moveHereBtn.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 14);
                    var moveBg = new global::Android.Graphics.Drawables.GradientDrawable();
                    moveBg.SetColor(Color.ParseColor("#1976D2"));
                    moveBg.SetCornerRadius(Dp(20));
                    moveHereBtn.Background = moveBg;
                    moveHereBtn.SetPadding(Dp(16), 0, Dp(16), 0);

                    cancelBtn.Click += (s, e) =>
                    {
                        HidePicker();
                        MarkFileAsProcessed(screenshotPath);
                        ShowToast("Screenshot kept in original location");
                    };

                    newFolderBtn.Click += (s, e) => ShowNewFolderOverlay(screenshotPath, currentPath);

                    moveHereBtn.Click += async (s, e) =>
                    {
                        HidePicker();
                        await MoveToFolder(screenshotPath, currentPath);
                    };

                    var btnParams = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
                    buttonRow.AddView(cancelBtn, btnParams);
                    buttonRow.AddView(newFolderBtn, btnParams);
                    buttonRow.AddView(moveHereBtn, btnParams);
                    card.AddView(buttonRow);

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

                    var density = Resources?.DisplayMetrics?.Density ?? 1f;
                    int Dp(int dp) => (int)(dp * density);

                    var card = new LinearLayout(this) { Orientation = Orientation.Vertical };
                    var cardBg = new global::Android.Graphics.Drawables.GradientDrawable();
                    cardBg.SetColor(Color.ParseColor("#FFFFFF"));
                    cardBg.SetCornerRadius(Dp(20));
                    card.Background = cardBg;
                    card.SetPadding(Dp(20), Dp(20), Dp(20), Dp(12));

                    var title = new TextView(this) { Text = "📁 Create new folder" };
                    title.SetTextColor(Color.ParseColor("#1A1A1A"));
                    title.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 18);
                    title.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
                    card.AddView(title);

                    var input = new EditText(this) { Hint = "Folder name" };
                    input.SetTextColor(Color.ParseColor("#1A1A1A"));
                    card.AddView(input);

                    var buttonRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
                    buttonRow.SetPadding(0, Dp(10), 0, 0);

                    var backBtn = new AndroidButton(this) { Text = "Back" };
                    backBtn.SetTextColor(Color.ParseColor("#757575"));
                    backBtn.SetBackgroundColor(Color.Transparent);
                    backBtn.SetAllCaps(false);

                    var createBtn = new AndroidButton(this) { Text = "Create" };
                    createBtn.SetTextColor(Color.White);
                    createBtn.SetAllCaps(false);
                    var createBg = new global::Android.Graphics.Drawables.GradientDrawable();
                    createBg.SetColor(Color.ParseColor("#1976D2"));
                    createBg.SetCornerRadius(Dp(20));
                    createBtn.Background = createBg;

                    backBtn.Click += (s, e) => ShowFolderPickerOverlay(screenshotPath, parentPath);

                    createBtn.Click += (s, e) =>
                    {
                        var name = input.Text?.Trim();
                        if (string.IsNullOrEmpty(name))
                        {
                            ShowToast("Enter a folder name");
                            return;
                        }

                        try
                        {
                            var newPath = IOPath.Combine(parentPath, name);
                            Directory.CreateDirectory(newPath);
                            ShowFolderPickerOverlay(screenshotPath, newPath);
                        }
                        catch (Exception ex)
                        {
                            ShowToast($"Failed to create folder: {ex.Message}");
                        }
                    };

                    var btnParams = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
                    buttonRow.AddView(backBtn, btnParams);
                    buttonRow.AddView(createBtn, btnParams);
                    card.AddView(buttonRow);

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

            int screenWidth = Resources?.DisplayMetrics?.WidthPixels ?? 1080;
            var density = Resources?.DisplayMetrics?.Density ?? 1f;
            var screenWidthDp = screenWidth / density;

            int targetWidth = screenWidthDp >= 600
                ? (int)(screenWidth * 0.5f)
                : (int)(screenWidth * 0.88f);

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