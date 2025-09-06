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
                    StartFolderPicker();
                }
                else
                {
                    Toast.MakeText(this, "Screenshot no longer exists", ToastLength.Short)?.Show();
                    // Changed: Use Finish() instead of FinishAffinity() to only close this activity
                    Finish();
                }
            }
        }

        private void StartDefaultFolderPicker()
        {
            try
            {
                var intent = new Intent(Intent.ActionOpenDocumentTree);
                // Remove ExcludeFromRecents - let the folder picker appear normally
                StartActivityForResult(intent, DEFAULT_FOLDER_PICKER_REQUEST);
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
                var intent = new Intent(Intent.ActionOpenDocumentTree);
                // Remove ExcludeFromRecents - let the folder picker appear normally
                StartActivityForResult(intent, FOLDER_PICKER_REQUEST);
            }
            catch (Exception ex)
            {
                Toast.MakeText(this, "Unable to open folder picker", ToastLength.Long)?.Show();
                _isWaitingForFolderPicker = false;
                // Changed: Use Finish() instead of FinishAffinity()
                Finish();
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

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);

            try
            {
                if (requestCode == DEFAULT_FOLDER_PICKER_REQUEST)
                {
                    _isSettingDefaultFolder = false;

                    if (resultCode == Result.Ok && data?.Data != null)
                    {
                        HandleDefaultFolderSelection(data.Data);
                    }


                    // Always notify MainPage that folder setup is complete
                    NotifyMainPageFolderSet();

                    // Close this activity since we're done - but don't use FinishAffinity
                    Finish();

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
                        // Changed: Use Finish() instead of FinishAffinity()
                        Finish();
                    }

                    
                }
            }
            catch (Exception ex)
            {
                _isWaitingForFolderPicker = false;
                _isSettingDefaultFolder = false;
                Toast.MakeText(this, $"Error: {ex.Message}", ToastLength.Long)?.Show();

                if (requestCode == DEFAULT_FOLDER_PICKER_REQUEST)
                {
                    NotifyMainPageFolderSet();
                    Finish();
                }
                else
                {
                    // Changed: Use Finish() instead of FinishAffinity()
                    Finish();
                }
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
               
            }
            catch (Exception ex)
            {
                Toast.MakeText(this, $"Error setting default folder: {ex.Message}", ToastLength.Long)?.Show();
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
                    // Changed: Use Finish() instead of FinishAffinity()
                    Finish();
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
                // Changed: Use Finish() instead of FinishAffinity()
                Finish();
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
                // Changed: Use Finish() instead of FinishAffinity()
                Finish();
            }
            catch (Exception ex)
            {
                ShowToast($"❌ Failed to move: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ Move error: {ex.Message}");
                // Changed: Use Finish() instead of FinishAffinity()
                Finish();
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