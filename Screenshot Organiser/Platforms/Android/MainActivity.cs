using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace Screenshot_Organiser
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        private const int FOLDER_PICKER_REQUEST = 1001;

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
        {
            base.OnActivityResult(requestCode, resultCode, data);

            if (requestCode == FOLDER_PICKER_REQUEST && resultCode == Result.Ok && data?.Data != null)
            {
                HandleFolderSelection(data.Data);
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
                Android.Widget.Toast.MakeText(this, $"Error: {ex.Message}", Android.Widget.ToastLength.Long)?.Show();
            }
        }

        private async Task MoveScreenshotToSelectedFolder(string screenshotPath, Android.Net.Uri folderUri)
        {
            // Simple approach - extract folder path from URI and move file there
            var folderPath = GetRealPathFromUri(folderUri);
            if (!string.IsNullOrEmpty(folderPath))
            {
                await MoveFileToFolder(screenshotPath, folderPath);
            }
        }

        private string GetRealPathFromUri(Android.Net.Uri uri)
        {
            // Extract real path from content URI
            return uri.Path?.Replace("/tree/primary:", "/storage/emulated/0/");
        }

        private async Task MoveFileToFolder(string sourcePath, string destinationFolder)
        {
            try
            {
                Directory.CreateDirectory(destinationFolder);
                var fileName = Path.GetFileName(sourcePath);
                var destinationPath = Path.Combine(destinationFolder, fileName);

                File.Copy(sourcePath, destinationPath, overwrite: true);
                File.Delete(sourcePath);
                Android.Widget.Toast.MakeText(this, $"✅ Screenshot moved to selected folder", Android.Widget.ToastLength.Long)?.Show();
                FinishAffinity();
            }
            catch (Exception ex)
            {
                Android.Widget.Toast.MakeText(this, $"❌ Failed to move: {ex.Message}", Android.Widget.ToastLength.Long)?.Show();
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
