using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Widget;

namespace Screenshot_Organiser
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
        ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        private const int FOLDER_PICKER_REQUEST = 1001;
        private const int DEFAULT_FOLDER_PICKER_REQUEST = 1002;
        private bool _isSettingDefaultFolder = false;
        private bool _isWaitingForFolderPicker = false;
        private string _pendingScreenshotPath = null;
        private bool _wasLaunchedForFolderSelection = false; // Track if launched specifically for folder selection

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Reset state
            _isWaitingForFolderPicker = false;
            _pendingScreenshotPath = null;
            _wasLaunchedForFolderSelection = false;

            // Process any pending requests
            ProcessIntent();
        }

        protected override void OnNewIntent(Intent intent)
        {
            base.OnNewIntent(intent);
            Intent = intent; // Update the current intent

            // Process the new intent
            ProcessIntent();
        }

        private void ProcessIntent()
        {
            var action = Intent?.GetStringExtra("action");

            if (action == "setup_default_folder" && !_isSettingDefaultFolder)
            {
                _isSettingDefaultFolder = true;
                _wasLaunchedForFolderSelection = true; // Mark that this was launched for folder selection
                StartDefaultFolderPicker();
            }
            else if (action == "pick_folder" && !_isWaitingForFolderPicker)
            {
                // Get pending screenshot
                var prefs = GetSharedPreferences("screenshot_prefs", FileCreationMode.Private);
                _pendingScreenshotPath = prefs?.GetString("pending_screenshot", null);

                if (!string.IsNullOrEmpty(_pendingScreenshotPath) && File.Exists(_pendingScreenshotPath))
                {
                    _isWaitingForFolderPicker = true;
                    _wasLaunchedForFolderSelection = true; // Mark that this was launched for folder selection
                    StartFolderPicker();
                }
                else
                {
                    Toast.MakeText(this, "Screenshot no longer exists", ToastLength.Short)?.Show();
                    Finish();
                }
            }
        }

        private const string STORAGE_ROOT = "/storage/emulated/0";

        private void StartDefaultFolderPicker()
        {
            try
            {
                ShowFolderPickerDialog(STORAGE_ROOT,
                    onFolderSelected: folderPath =>
                    {
                        _isSettingDefaultFolder = false;
                        SetDefaultScreenshotFolder(folderPath);
                        NotifyMainPageFolderSet();
                        System.Diagnostics.Debug.WriteLine("Default folder setup completed, staying in app");
                    },
                    onCancelled: () =>
                    {
                        _isSettingDefaultFolder = false;
                        NotifyMainPageFolderSet();
                    });
            }
            catch (Exception ex)
            {
                Toast.MakeText(this, "Unable to open folder picker", ToastLength.Long)?.Show();
                _isSettingDefaultFolder = false;
                NotifyMainPageFolderSet();
            }
        }

        private void StartFolderPicker()
        {
            try
            {
                ShowFolderPickerDialog(STORAGE_ROOT,
                    onFolderSelected: async folderPath =>
                    {
                        _isWaitingForFolderPicker = false;
                        await HandleFolderPathSelection(folderPath);
                    },
                    onCancelled: () =>
                    {
                        _isWaitingForFolderPicker = false;
                        Toast.MakeText(this, "Folder selection cancelled", ToastLength.Short)?.Show();
                        ClearPendingScreenshot();

                        if (_wasLaunchedForFolderSelection)
                        {
                            Finish();
                        }
                    });
            }
            catch (Exception ex)
            {
                Toast.MakeText(this, "Unable to open folder picker", ToastLength.Long)?.Show();
                _isWaitingForFolderPicker = false;
                Finish();
            }
        }

        private void ShowFolderPickerDialog(string currentPath, Action<string> onFolderSelected, Action onCancelled)
        {
            try
            {
                string[] subFolders;
                try
                {
                    subFolders = Directory.GetDirectories(currentPath)
                        .Where(d => !Path.GetFileName(d).StartsWith("."))
                        .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }
                catch
                {
                    subFolders = Array.Empty<string>();
                }

                bool canGoUp = !string.Equals(currentPath.TrimEnd('/'), STORAGE_ROOT, StringComparison.OrdinalIgnoreCase);

                var items = new List<string>();
                if (canGoUp) items.Add("⬆️ ..");
                items.AddRange(subFolders.Select(d => "📁 " + Path.GetFileName(d)));

                var builder = new AlertDialog.Builder(this);
                builder.SetTitle(currentPath.Replace(STORAGE_ROOT, "Internal storage"));

                builder.SetItems(items.ToArray(), (s, e) =>
                {
                    if (canGoUp && e.Which == 0)
                    {
                        var parent = Path.GetDirectoryName(currentPath.TrimEnd('/')) ?? STORAGE_ROOT;
                        ShowFolderPickerDialog(parent, onFolderSelected, onCancelled);
                    }
                    else
                    {
                        var index = canGoUp ? e.Which - 1 : e.Which;
                        ShowFolderPickerDialog(subFolders[index], onFolderSelected, onCancelled);
                    }
                });

                builder.SetPositiveButton("Use this folder", (s, e) => onFolderSelected(currentPath));
                builder.SetNeutralButton("New folder", (s, e) => ShowNewFolderDialog(currentPath, onFolderSelected, onCancelled));
                builder.SetNegativeButton("Cancel", (s, e) => onCancelled());
                builder.SetCancelable(false);
                builder.Show();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing folder picker: {ex.Message}");
                onCancelled();
            }
        }

        private void ShowNewFolderDialog(string parentPath, Action<string> onFolderSelected, Action onCancelled)
        {
            var input = new EditText(this) { Hint = "Folder name" };
            var builder = new AlertDialog.Builder(this);
            builder.SetTitle("Create new folder");
            builder.SetView(input);
            builder.SetPositiveButton("Create", (s, e) =>
            {
                var name = input.Text?.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    ShowFolderPickerDialog(parentPath, onFolderSelected, onCancelled);
                    return;
                }

                try
                {
                    var newPath = Path.Combine(parentPath, name);
                    Directory.CreateDirectory(newPath);
                    ShowFolderPickerDialog(newPath, onFolderSelected, onCancelled);
                }
                catch (Exception ex)
                {
                    Toast.MakeText(this, $"Failed to create folder: {ex.Message}", ToastLength.Long)?.Show();
                    ShowFolderPickerDialog(parentPath, onFolderSelected, onCancelled);
                }
            });
            builder.SetNegativeButton("Back", (s, e) => ShowFolderPickerDialog(parentPath, onFolderSelected, onCancelled));
            builder.SetCancelable(false);
            builder.Show();
        }

        private async Task HandleFolderPathSelection(string folderPath)
        {
            try
            {
                if (string.IsNullOrEmpty(_pendingScreenshotPath) || !File.Exists(_pendingScreenshotPath))
                {
                    Toast.MakeText(this, "Screenshot no longer exists", ToastLength.Short)?.Show();
                    ClearPendingScreenshot();

                    if (_wasLaunchedForFolderSelection)
                    {
                        Finish();
                    }
                    return;
                }

                await MoveFileToFolder(_pendingScreenshotPath, folderPath);
                ClearPendingScreenshot();
            }
            catch (Exception ex)
            {
                Toast.MakeText(this, $"Error: {ex.Message}", ToastLength.Long)?.Show();
                ClearPendingScreenshot();

                if (_wasLaunchedForFolderSelection)
                {
                    Finish();
                }
            }
        }

        private void SetDefaultScreenshotFolder(string folderPath)
        {
            var prefs = GetSharedPreferences("screenshot_prefs", FileCreationMode.Private);
            prefs?.Edit()?.PutString("default_screenshot_folder", folderPath)?.Apply();

            Toast.MakeText(this, $"Default screenshot folder set: {Path.GetFileName(folderPath)}",
                ToastLength.Long)?.Show();
        }

        private void NotifyMainPageFolderSet()
        {
            try
            {
                // Notify MainPage that folder setup is complete
                if (Microsoft.Maui.Controls.Application.Current?.MainPage is AppShell shell &&
                    shell.CurrentPage is MainPage mainPage)
                {
                    mainPage.OnDefaultFolderSet();
                }
                else if (Microsoft.Maui.Controls.Application.Current?.MainPage is MainPage directMainPage)
                {
                    directMainPage.OnDefaultFolderSet();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error notifying MainPage: {ex.Message}");
            }
        }

        private void ClearPendingScreenshot()
        {
            try
            {
                var prefs = GetSharedPreferences("screenshot_prefs", FileCreationMode.Private);
                prefs?.Edit()?.Remove("pending_screenshot")?.Apply();
                _pendingScreenshotPath = null;
            }
            catch (Exception ex)
            {
                Toast.MakeText(this, $"Error: {ex.Message}", ToastLength.Long)?.Show();
            }
        }

        private async Task MoveFileToFolder(string sourcePath, string destinationFolder)
        {
            try
            {
                Directory.CreateDirectory(destinationFolder);
                var fileName = Path.GetFileName(sourcePath);
                var destinationPath = Path.Combine(destinationFolder, fileName);

                // Handle duplicate names
                int counter = 1;
                while (File.Exists(destinationPath))
                {
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                    var extension = Path.GetExtension(fileName);
                    destinationPath = Path.Combine(destinationFolder, $"{nameWithoutExt}_{counter}{extension}");
                    counter++;
                }

                // Mark as moved before moving
                ModernScreenshotMonitor.MarkFileAsMoved(sourcePath);

                File.Copy(sourcePath, destinationPath, overwrite: true);
                File.Delete(sourcePath);

                Toast.MakeText(this, $"✅ Screenshot moved to {Path.GetFileName(destinationFolder)}",
                    ToastLength.Long)?.Show();

                if (_wasLaunchedForFolderSelection)
                {
                    FinishAndRemoveTask();
                }
            }
            catch (Exception ex)
            {
                ShowToast($"❌ Failed to move: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ Move error: {ex.Message}");

                if (_wasLaunchedForFolderSelection)
                {
                    Finish();
                }
            }
        }

        private void ShowToast(string message)
        {
            try
            {
                Toast.MakeText(this, message, ToastLength.Long)?.Show();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing toast: {ex.Message}");
            }
        }

        protected override void OnResume()
        {
            base.OnResume();
        }
    }
}