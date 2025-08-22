using Android.Provider;
using Screenshot_Organiser.Platforms.Android;
using System.Timers;

namespace Screenshot_Organiser;

public class ScreenshotEventArgs : EventArgs
{
    public string FilePath { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public class ModernScreenshotMonitor : IDisposable
{
    private System.Timers.Timer? _timer;
    private DateTime _lastCheckTime;
    private readonly HashSet<string> _processedFiles = new();
    private readonly HashSet<string> _movedFiles = new();
    private string? _defaultScreenshotFolder;

    public event EventHandler<ScreenshotEventArgs>? ScreenshotDetected;
    public bool IsMonitoring => _timer?.Enabled ?? false;

    public Task StartMonitoring()
    {
        return Task.Run(() =>
        {
            _lastCheckTime = DateTime.Now;
            LoadDefaultScreenshotFolder();

            // Start the overlay service as a foreground service
            StartOverlayService();

            _timer = new System.Timers.Timer(3000);
            _timer.Elapsed += CheckForNewScreenshots;
            _timer.AutoReset = true;
            _timer.Start();
        });
    }

    private void LoadDefaultScreenshotFolder()
    {
        try
        {
            var context = Platform.CurrentActivity ?? Android.App.Application.Context;
            var prefs = context.GetSharedPreferences("screenshot_prefs", Android.Content.FileCreationMode.Private);
            _defaultScreenshotFolder = prefs?.GetString("default_screenshot_folder",
                "/storage/emulated/0/Pictures/Screenshots");

            System.Diagnostics.Debug.WriteLine($"Monitoring folder: {_defaultScreenshotFolder}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading default folder: {ex.Message}");
            _defaultScreenshotFolder = "/storage/emulated/0/Pictures/Screenshots";
        }
    }

    public Task StopMonitoring()
    {
        return Task.Run(() =>
        {
            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;
            _processedFiles.Clear();
            _movedFiles.Clear();

            // Stop the overlay service
            StopOverlayService();
        });
    }

    private void StartOverlayService()
    {
        try
        {
            var context = Platform.CurrentActivity ?? Android.App.Application.Context;
            var intent = new Android.Content.Intent(context, typeof(OverlayService));

            // Start as foreground service for persistence
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
            {
                context.StartForegroundService(intent);
            }
            else
            {
                context.StartService(intent);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error starting overlay service: {ex.Message}");
        }
    }

    private void StopOverlayService()
    {
        try
        {
            var context = Platform.CurrentActivity ?? Android.App.Application.Context;
            var intent = new Android.Content.Intent(context, typeof(OverlayService));
            context.StopService(intent);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error stopping overlay service: {ex.Message}");
        }
    }

    // Method to restart monitoring if service was killed
    public static void RestartMonitoringIfNeeded()
    {
        try
        {
            var context = Platform.CurrentActivity ?? Android.App.Application.Context;

            // Check if service is already running
            var activityManager = context.GetSystemService(Android.Content.Context.ActivityService) as Android.App.ActivityManager;
            var runningServices = activityManager?.GetRunningServices(int.MaxValue);

            bool serviceRunning = runningServices?.Any(service =>
                service.Service.ClassName.Contains("OverlayService")) ?? false;

            if (!serviceRunning)
            {
                var intent = new Android.Content.Intent(context, typeof(OverlayService));

                if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
                {
                    context.StartForegroundService(intent);
                }
                else
                {
                    context.StartService(intent);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error checking/restarting service: {ex.Message}");
        }
    }

    // Method to mark a file as moved (call this from OverlayService after moving)
    public static void MarkFileAsMoved(string filePath)
    {
        // Add to a static collection or use shared preferences
        try
        {
            var context = Platform.CurrentActivity ?? Android.App.Application.Context;
            var prefs = context.GetSharedPreferences("screenshot_prefs", Android.Content.FileCreationMode.Private);
            var editor = prefs?.Edit();
            editor?.PutLong($"moved_{filePath.GetHashCode()}", DateTimeOffset.Now.ToUnixTimeMilliseconds());
            editor?.Apply();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error marking file as moved: {ex.Message}");
        }
    }

    private bool IsFileMoved(string filePath)
    {
        try
        {
            var context = Platform.CurrentActivity ?? Android.App.Application.Context;
            var prefs = context.GetSharedPreferences("screenshot_prefs", Android.Content.FileCreationMode.Private);
            var moveTime = prefs?.GetLong($"moved_{filePath.GetHashCode()}", 0) ?? 0;

            if (moveTime > 0)
            {
                // If file was marked as moved within last 5 minutes, consider it moved
                var moveDateTime = DateTimeOffset.FromUnixTimeMilliseconds(moveTime);
                return DateTime.Now.Subtract(moveDateTime.DateTime).TotalMinutes < 5;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error checking if file is moved: {ex.Message}");
        }
        return false;
    }

    private void CheckForNewScreenshots(object? sender, ElapsedEventArgs e)
    {
        try
        {
            // Ensure service is still running (restart if needed)
            RestartMonitoringIfNeeded();

            // Only monitor the default screenshot folder
            HashSet<string> screenshots = GetRecentScreenshotsFromDefaultFolder();
            _lastCheckTime = DateTime.Now;

            foreach (var screenshot in screenshots)
            {
                if (File.Exists(screenshot))
                {
                    // Skip if already processed or recently moved
                    if (!_processedFiles.Contains(screenshot) && !IsFileMoved(screenshot))
                    {
                        _processedFiles.Add(screenshot);

                        System.Diagnostics.Debug.WriteLine($"New screenshot detected: {screenshot}");

                        // Show overlay and raise event
                        ShowScreenshotOverlay(screenshot);
                        ScreenshotDetected?.Invoke(this, new ScreenshotEventArgs
                        {
                            FilePath = screenshot,
                            Timestamp = DateTime.Now
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error checking screenshots: {ex.Message}");
        }
    }

    private void ShowScreenshotOverlay(string screenshotPath)
    {
        try
        {
            OverlayService.Instance?.ShowScreenshotDialog(screenshotPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error showing overlay: {ex.Message}");
        }
    }

    private HashSet<string> GetRecentScreenshotsFromDefaultFolder()
    {
        var screenshots = new HashSet<string>();

        if (string.IsNullOrEmpty(_defaultScreenshotFolder))
        {
            return screenshots;
        }

        try
        {
            screenshots = GetScreenshotsFromSpecificDirectory(_defaultScreenshotFolder);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting screenshots from default folder: {ex.Message}");
        }

        return screenshots;
    }

    private HashSet<string> GetScreenshotsFromSpecificDirectory(string folderPath)
    {
        var screenshots = new HashSet<string>();

        try
        {
            if (!Directory.Exists(folderPath))
            {
                System.Diagnostics.Debug.WriteLine($"Screenshot folder doesn't exist: {folderPath}");
                return screenshots;
            }

            // Get files modified after last check
            var recentFiles = Directory.GetFiles(folderPath, "*.png")
                .Concat(Directory.GetFiles(folderPath, "*.jpg"))
                .Concat(Directory.GetFiles(folderPath, "*.jpeg"))
                .Where(f =>
                {
                    var createdTime = File.GetCreationTime(f);
                    var modifiedTime = File.GetLastWriteTime(f);
                    var latestTime = createdTime > modifiedTime ? createdTime : modifiedTime;
                    return latestTime > _lastCheckTime;
                })
                .Where(f => IsScreenshotFile(f, Path.GetFileName(f)));

            foreach (var file in recentFiles)
            {
                screenshots.Add(file);
            }

            System.Diagnostics.Debug.WriteLine($"Found {screenshots.Count} new screenshots in {folderPath}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error checking directory {folderPath}: {ex.Message}");
        }

        return screenshots;
    }

    private static bool IsScreenshotFile(string filePath, string fileName)
    {
        var name = fileName?.ToLowerInvariant() ?? Path.GetFileName(filePath).ToLowerInvariant();
        var path = filePath.ToLowerInvariant();

        // More specific screenshot detection
        return name.Contains("screenshot") ||
               name.StartsWith("screen_") ||
               name.StartsWith("scrnshot") ||
               name.Contains("screen-") ||
               path.Contains("/screenshots/") ||
               path.Contains("/screenshot/");
    }

    public void Dispose()
    {
        StopMonitoring().Wait();
        GC.SuppressFinalize(this);
    }
}