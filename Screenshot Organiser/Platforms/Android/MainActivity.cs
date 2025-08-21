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

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Reset state
            _isWaitingForFolderPicker = false;
            _pendingScreenshotPath = null;

            // Check if default screenshot folder is set
            CheckDefaultScreenshotFolder();

            // Process any pending folder picker request
            ProcessFolderPickerRequest();
        }

        protected override void OnNewIntent(Intent intent)
        {
            base.OnNewIntent(intent);
            Intent = intent; // Update the current intent

            // Process the new intent
            ProcessFolderPickerRequest();
        }

        private void ProcessFolderPickerRequest()
        {
            var action = Intent?.GetStringExtra("action");

            if (action == "pick_folder" && !_isWaitingForFolderPicker)
            {
                // Get pending screenshot
                var prefs = GetSharedPreferences("screenshot_prefs", FileCreationMode.Private);
                _pendingScreenshotPath = prefs?.GetString("pending_screenshot", null);

                if (!string.IsNullOrEmpty(_pendingScreenshotPath) && File.Exists(_pendingScreenshotPath))
                {
                    _isWaitingForFolderPicker = true;
                    StartFolderPicker();
                }
                else
                {
                    Toast.MakeText(this, "Screenshot no longer exists", ToastLength.Short)?.Show();
                    FinishAffinity();
                }
            }
        }

        private void CheckDefaultScreenshotFolder()
        {
            var prefs = GetSharedPreferences("screenshot_prefs", FileCreationMode.Private);
            var defaultFolder = prefs?.GetString("default_screenshot_folder", null);

            if (string.IsNullOrEmpty(defaultFolder))
            {
                // Show dialog to set default folder
                ShowDefaultFolderSetupDialog();
            }
        }

        private void ShowDefaultFolderSetupDialog()
        {
            var dialog = new AlertDialog.Builder(this)
                .SetTitle("Setup Default Screenshot Folder")
                .SetMessage("Please select the folder where your device saves screenshots (usually Pictures/Screenshots)")
                .SetPositiveButton("Select Folder", (sender, args) =>
                {
                    _isSettingDefaultFolder = true;
                    StartDefaultFolderPicker();
                })
                .SetNegativeButton("Use Default", (sender, args) =>
                {
                    // Set common default screenshot paths
                    SetDefaultScreenshotFolder("/storage/emulated/0/Pictures/Screenshots");
                })
                .SetCancelable(false)
                .Create();

            dialog.Show();
        }

        private void StartDefaultFolderPicker()
        {
            try
            {
                var intent = new Intent(Intent.ActionOpenDocumentTree);
                StartActivityForResult(intent, DEFAULT_FOLDER_PICKER_REQUEST);
            }
            catch (Exception ex)
            {
                Toast.MakeText(this, "Unable to open folder picker", ToastLength.Long)?.Show();
            }
        }

        private void StartFolderPicker()
        {
            try
            {
                var intent = new Intent(Intent.ActionOpenDocumentTree);
                StartActivityForResult(intent, FOLDER_PICKER_REQUEST);
            }
            catch (Exception ex)
            {
                Toast.MakeText(this, "Unable to open folder picker", ToastLength.Long)?.Show();
                _isWaitingForFolderPicker = false;
                FinishAffinity();
            }
        }

        private void SetDefaultScreenshotFolder(string folderPath)
        {
            var prefs = GetSharedPreferences("screenshot_prefs", FileCreationMode.Private);
            prefs?.Edit()?.PutString("default_screenshot_folder", folderPath)?.Apply();

            Toast.MakeText(this, $"Default screenshot folder set: {Path.GetFileName(folderPath)}",
                ToastLength.Long)?.Show();
        }

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);


            try
            {
                if (requestCode == DEFAULT_FOLDER_PICKER_REQUEST)
                {
                    if (resultCode == Result.Ok && data?.Data != null)
                    {
                        HandleDefaultFolderSelection(data.Data);
                    }
                    else
                    {
                        _isSettingDefaultFolder = false;
                    }
                }
                else if (requestCode == FOLDER_PICKER_REQUEST)
                {
                    _isWaitingForFolderPicker = false;

                    if (resultCode == Result.Ok && data?.Data != null)
                    {
                        HandleFolderSelection(data.Data);
                    }
                    else
                    {
                        Toast.MakeText(this, "Folder selection cancelled", ToastLength.Short)?.Show();
                        ClearPendingScreenshot();
                        FinishAffinity();
                    }
                }
            }
            catch (Exception ex)
            {
                _isWaitingForFolderPicker = false;
                _isSettingDefaultFolder = false;
                FinishAffinity();
            }
        }

        private void HandleDefaultFolderSelection(Android.Net.Uri folderUri)
        {
            try
            {
                var folderPath = GetRealPathFromUri(folderUri);
                if (!string.IsNullOrEmpty(folderPath))
                {
                    SetDefaultScreenshotFolder(folderPath);
                }
                _isSettingDefaultFolder = false;
            }
            catch (Exception ex)
            {
                Toast.MakeText(this, $"Error setting default folder: {ex.Message}", ToastLength.Long)?.Show();
                _isSettingDefaultFolder = false;
            }
        }

        private async void HandleFolderSelection(Android.Net.Uri folderUri)
        {
            try
            {

                if (string.IsNullOrEmpty(_pendingScreenshotPath) || !File.Exists(_pendingScreenshotPath))
                {
                    Toast.MakeText(this, "Screenshot no longer exists", ToastLength.Short)?.Show();
                    ClearPendingScreenshot();
                    FinishAffinity();
                    return;
                }

                // Move screenshot to selected folder
                await MoveScreenshotToSelectedFolder(_pendingScreenshotPath, folderUri);

                // Clear pending screenshot
                ClearPendingScreenshot();
            }
            catch (Exception ex)
            {
                Toast.MakeText(this, $"Error: {ex.Message}", ToastLength.Long)?.Show();
                ClearPendingScreenshot();
                FinishAffinity();
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

        private async Task MoveScreenshotToSelectedFolder(string screenshotPath, Android.Net.Uri folderUri)
        {
            var folderPath = GetRealPathFromUri(folderUri);
            if (!string.IsNullOrEmpty(folderPath))
            {
                await MoveFileToFolder(screenshotPath, folderPath);
            }
            else
            {
                Toast.MakeText(this, "Unable to access selected folder", ToastLength.Long)?.Show();
            }
        }

        private string GetRealPathFromUri(Android.Net.Uri uri)
        {
            try
            {
                var path = uri.Path?.Replace("/tree/primary:", "/storage/emulated/0/");
                return path;
            }
            catch (Exception ex)
            {
                return null;
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
                FinishAffinity();
            }
            catch (Exception ex)
            {
                Toast.MakeText(this, $"❌ Failed to move: {ex.Message}", ToastLength.Long)?.Show();
                FinishAffinity();
            }
        }
    }
}