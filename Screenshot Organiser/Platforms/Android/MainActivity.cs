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

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Check if default screenshot folder is set
            CheckDefaultScreenshotFolder();
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
            var intent = new Intent(Intent.ActionOpenDocumentTree);
            StartActivityForResult(intent, DEFAULT_FOLDER_PICKER_REQUEST);
        }

        private void SetDefaultScreenshotFolder(string folderPath)
        {
            var prefs = GetSharedPreferences("screenshot_prefs", FileCreationMode.Private);
            prefs?.Edit()?.PutString("default_screenshot_folder", folderPath)?.Apply();

            Toast.MakeText(this, $"Default screenshot folder set: {Path.GetFileName(folderPath)}",
                ToastLength.Long)?.Show();
        }

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
        {
            base.OnActivityResult(requestCode, resultCode, data);

            if (requestCode == DEFAULT_FOLDER_PICKER_REQUEST && resultCode == Result.Ok && data?.Data != null)
            {
                HandleDefaultFolderSelection(data.Data);
            }
            else if (requestCode == FOLDER_PICKER_REQUEST && resultCode == Result.Ok && data?.Data != null)
            {
                HandleFolderSelection(data.Data);
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
                // Get pending screenshot
                var prefs = GetSharedPreferences("screenshot_prefs", FileCreationMode.Private);
                var screenshotPath = prefs?.GetString("pending_screenshot", null);
                if (string.IsNullOrEmpty(screenshotPath) || !File.Exists(screenshotPath))
                {
                    return;
                }

                // Move screenshot to selected folder
                await MoveScreenshotToSelectedFolder(screenshotPath, folderUri);

                // Clear pending screenshot
                prefs?.Edit()?.Remove("pending_screenshot")?.Apply();
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
        }

        private string GetRealPathFromUri(Android.Net.Uri uri)
        {
            return uri.Path?.Replace("/tree/primary:", "/storage/emulated/0/");
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

                File.Copy(sourcePath, destinationPath, overwrite: true);
                File.Delete(sourcePath);

                Toast.MakeText(this, $"✅ Screenshot moved to {Path.GetFileName(destinationFolder)}",
                    ToastLength.Long)?.Show();
                FinishAffinity();
            }
            catch (Exception ex)
            {
                Toast.MakeText(this, $"❌ Failed to move: {ex.Message}", ToastLength.Long)?.Show();
            }
        }

        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            if (intent?.GetStringExtra("action") == "pick_folder")
            {
                StartFolderPicker();
            }
        }

        private void StartFolderPicker()
        {
            var intent = new Intent(Intent.ActionOpenDocumentTree);
            StartActivityForResult(intent, FOLDER_PICKER_REQUEST);
        }
    }
}