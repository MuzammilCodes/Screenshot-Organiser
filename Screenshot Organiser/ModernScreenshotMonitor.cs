using Android.Database;
using Android.Provider;
using AndroidX.Core.Content;
using System.Timers;
using Screenshot_Organiser.Platforms.Android;

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
    private readonly List<string> _processedFiles = new();

    public event EventHandler<ScreenshotEventArgs>? ScreenshotDetected;
    public bool IsMonitoring => _timer?.Enabled ?? false;

    public Task StartMonitoring()
    {
        return Task.Run(() =>
        {
            _lastCheckTime = DateTime.Now;

            // Start the overlay service
            StartOverlayService();

            _timer = new System.Timers.Timer(2000); // Check every 2 seconds
            _timer.Elapsed += CheckForNewScreenshots;
            _timer.AutoReset = true;
            _timer.Start();
        });
    }

    public Task StopMonitoring()
    {
        return Task.Run(() =>
        {
            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;
            _processedFiles.Clear();

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

            // Don't use StartForegroundService - just use regular StartService
            context.StartService(intent);
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

    private void CheckForNewScreenshots(object? sender, ElapsedEventArgs e)
    {
        try
        {
            var screenshots = GetRecentScreenshots();

            foreach (var screenshot in screenshots)
            {
                if (!_processedFiles.Contains(screenshot))
                {
                    _processedFiles.Add(screenshot);

                    // Show overlay instead of raising event
                    ShowScreenshotOverlay(screenshot);

                    ScreenshotDetected?.Invoke(this, new ScreenshotEventArgs
                    {
                        FilePath = screenshot,
                        Timestamp = DateTime.Now
                    });
                }
            }

            _lastCheckTime = DateTime.Now;
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
            // Show overlay dialog via the service
            OverlayService.Instance?.ShowScreenshotDialog(screenshotPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error showing overlay: {ex.Message}");
        }
    }

    private List<string> GetRecentScreenshots()
    {
        var screenshots = new List<string>();

        try
        {
            var context = Platform.CurrentActivity ?? Android.App.Application.Context;
            var resolver = context.ContentResolver;

            // Query MediaStore for recent images
            var uri = MediaStore.Images.Media.ExternalContentUri;
            var projection = new[]
            {
                MediaStore.Images.Media.InterfaceConsts.Data,
                MediaStore.Images.Media.InterfaceConsts.DateAdded,
                MediaStore.Images.Media.InterfaceConsts.DisplayName
            };

            var selection = $"{MediaStore.Images.Media.InterfaceConsts.DateAdded} > ? AND " +
                          $"({MediaStore.Images.Media.InterfaceConsts.Data} LIKE '%/Pictures/Screenshots/%' OR " +
                          $"{MediaStore.Images.Media.InterfaceConsts.Data} LIKE '%screenshot%' OR " +
                          $"{MediaStore.Images.Media.InterfaceConsts.DisplayName} LIKE '%screenshot%')";

            var selectionArgs = new[] { ((DateTimeOffset)_lastCheckTime).ToUnixTimeSeconds().ToString() };
            var sortOrder = $"{MediaStore.Images.Media.InterfaceConsts.DateAdded} DESC";

            using var cursor = resolver?.Query(uri, projection, selection, selectionArgs, sortOrder);

            if (cursor != null && cursor.MoveToFirst())
            {
                var dataIndex = cursor.GetColumnIndex(MediaStore.Images.Media.InterfaceConsts.Data);
                var nameIndex = cursor.GetColumnIndex(MediaStore.Images.Media.InterfaceConsts.DisplayName);

                do
                {
                    var filePath = cursor.GetString(dataIndex);
                    var fileName = cursor.GetString(nameIndex);

                    if (!string.IsNullOrEmpty(filePath) &&
                        File.Exists(filePath) &&
                        IsScreenshotFile(filePath, fileName))
                    {
                        screenshots.Add(filePath);
                    }

                } while (cursor.MoveToNext());
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error querying MediaStore: {ex.Message}");

            // Fallback: Check screenshot directory directly
            screenshots.AddRange(GetScreenshotsFromDirectory());
        }

        return screenshots;
    }

    private List<string> GetScreenshotsFromDirectory()
    {
        var screenshots = new List<string>();

        try
        {
            var screenshotPaths = new[]
            {
                "/storage/emulated/0/Pictures/Screenshots",
                "/storage/emulated/0/DCIM/Screenshots",
                "/sdcard/Pictures/Screenshots",
                "/sdcard/DCIM/Screenshots"
            };

            foreach (var path in screenshotPaths.Where(Directory.Exists))
            {
                var files = Directory.GetFiles(path, "*.png")
                    .Concat(Directory.GetFiles(path, "*.jpg"))
                    .Where(f => File.GetCreationTime(f) > _lastCheckTime)
                    .Where(f => IsScreenshotFile(f, Path.GetFileName(f)));

                screenshots.AddRange(files);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error checking screenshot directories: {ex.Message}");
        }

        return screenshots;
    }

    private static bool IsScreenshotFile(string filePath, string fileName)
    {
        var name = fileName?.ToLowerInvariant() ?? Path.GetFileName(filePath).ToLowerInvariant();
        var path = filePath.ToLowerInvariant();

        return name.Contains("screenshot") ||
               name.StartsWith("screen_") ||
               name.StartsWith("scrnshot") ||
               path.Contains("/screenshots/") ||
               path.Contains("/screenshot/");
    }

    public void Dispose()
    {
        StopMonitoring().Wait();
        GC.SuppressFinalize(this);
    }
}