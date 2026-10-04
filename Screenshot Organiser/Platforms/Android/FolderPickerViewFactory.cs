using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using AndroidButton = Android.Widget.Button;
using AndroidView = Android.Views.View;
using AndroidPaint = Android.Graphics.Paint;
using Color = Android.Graphics.Color;
using ListView = Android.Widget.ListView;
using IOPath = System.IO.Path;

namespace Screenshot_Organiser.Platforms.Android
{
    /// <summary>
    /// Builds the shared, styled folder picker UI used both by the
    /// OverlayService (system overlay) and MainActivity (in-app dialog).
    /// Pure UI factory - the caller decides how the view is hosted.
    /// </summary>
    public static class FolderPickerViewFactory
    {
        public const string StorageRoot = "/storage/emulated/0";

        private const string AccentColor = "#1976D2";

        // Light palette - soft off-white, easy on the eyes
        private const string LightCardColor = "#FAFAFA";
        private const string LightTitleColor = "#1A1A1A";
        private const string LightSecondaryColor = "#757575";

        // Dark palette
        private const string DarkCardColor = "#313638";
        private const string DarkTitleColor = "#F5F5F5";
        private const string DarkSecondaryColor = "#B0B0B0";

        private static bool IsDarkMode(Context context)
        {
            var uiMode = context.Resources?.Configuration?.UiMode & global::Android.Content.Res.UiMode.NightMask;
            return uiMode == global::Android.Content.Res.UiMode.NightYes;
        }

        // Material You dynamic accent on Android 12+, app accent otherwise
        private static Color ThemeAccent(Context context)
        {
            if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.S)
            {
                try
                {
                    var resId = IsDarkMode(context)
                        ? global::Android.Resource.Color.SystemAccent1200
                        : global::Android.Resource.Color.SystemAccent1600;
                    return new Color(context.GetColor(resId));
                }
                catch
                {
                    // fall through to static accent
                }
            }
            return Color.ParseColor(AccentColor);
        }

        private static Color CardColor(Context context) =>
            Color.ParseColor(IsDarkMode(context) ? DarkCardColor : LightCardColor);

        private static Color TitleColor(Context context) =>
            Color.ParseColor(IsDarkMode(context) ? DarkTitleColor : LightTitleColor);

        private static Color SecondaryColor(Context context) =>
            Color.ParseColor(IsDarkMode(context) ? DarkSecondaryColor : LightSecondaryColor);

        /// <summary>
        /// Builds the folder browser card.
        /// </summary>
        /// <param name="context">Android context.</param>
        /// <param name="title">Card title, e.g. "📂 Move screenshot to…".</param>
        /// <param name="confirmText">Text of the primary button, e.g. "Move here".</param>
        /// <param name="currentPath">Folder currently shown.</param>
        /// <param name="onNavigate">Called with the new path when the user taps a folder or "..".</param>
        /// <param name="onConfirm">Called with the current path when the primary button is tapped.</param>
        /// <param name="onNewFolder">Called with the current path when "New folder" is tapped.</param>
        /// <param name="onCancel">Called when "Cancel" is tapped.</param>
        public static AndroidView BuildFolderPickerCard(
            Context context,
            string title,
            string confirmText,
            string currentPath,
            Action<string> onNavigate,
            Action<string> onConfirm,
            Action<string> onNewFolder,
            Action onCancel)
        {
            var density = context.Resources?.DisplayMetrics?.Density ?? 1f;
            int Dp(int dp) => (int)(dp * density);

            var card = CreateCard(context, Dp);

            card.AddView(CreateFolderTitle(context, title));

            // Current path subtitle
            var pathLabel = new TextView(context)
            {
                Text = currentPath.Replace(StorageRoot, "Internal storage")
            };
            pathLabel.SetTextColor(SecondaryColor(context));
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

            bool canGoUp = !string.Equals(currentPath.TrimEnd('/'), StorageRoot, StringComparison.OrdinalIgnoreCase);

            var items = new List<string>();
            if (canGoUp) items.Add("..");
            items.AddRange(subFolders.Select(d => IOPath.GetFileName(d)));

            var listView = new ListView(context)
            {
                Adapter = new FolderListAdapter(context, items, canGoUp),
                Divider = null
            };
            listView.ItemClick += (s, e) =>
            {
                try
                {
                    if (canGoUp && e.Position == 0)
                    {
                        var parent = IOPath.GetDirectoryName(currentPath.TrimEnd('/')) ?? StorageRoot;
                        onNavigate(parent);
                    }
                    else
                    {
                        var index = canGoUp ? e.Position - 1 : e.Position;
                        onNavigate(subFolders[index]);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error navigating folder: {ex.Message}");
                }
            };

            int screenHeight = context.Resources?.DisplayMetrics?.HeightPixels ?? 1920;
            var listParams = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, (int)(screenHeight * 0.35f));
            card.AddView(listView, listParams);

            // Buttons row
            var buttonRow = CreateButtonRow(context, Dp);

            var cancelBtn = CreateFlatButton(context, "Cancel", SecondaryColor(context));
            var newFolderBtn = CreateFlatButton(context, "New folder", Color.ParseColor(AccentColor));
            var confirmBtn = CreatePillButton(context, confirmText, Dp);

            cancelBtn.Click += (s, e) => onCancel();
            newFolderBtn.Click += (s, e) => onNewFolder(currentPath);
            confirmBtn.Click += (s, e) => onConfirm(currentPath);

            var btnParams = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
            buttonRow.AddView(cancelBtn, btnParams);
            buttonRow.AddView(newFolderBtn, btnParams);
            buttonRow.AddView(confirmBtn, btnParams);
            card.AddView(buttonRow);

            return card;
        }

        /// <summary>
        /// Builds the "create new folder" card.
        /// </summary>
        /// <param name="context">Android context.</param>
        /// <param name="parentPath">Folder in which the new folder will be created.</param>
        /// <param name="onCreated">Called with the new folder path once created.</param>
        /// <param name="onBack">Called when the user taps "Back" (return to picker at parentPath).</param>
        /// <param name="showMessage">Used to surface validation/error messages.</param>
        public static AndroidView BuildNewFolderCard(
            Context context,
            string parentPath,
            Action<string> onCreated,
            Action onBack,
            Action<string> showMessage)
        {
            var density = context.Resources?.DisplayMetrics?.Density ?? 1f;
            int Dp(int dp) => (int)(dp * density);

            var card = CreateCard(context, Dp);

            card.AddView(CreateFolderTitle(context, "Create new folder"));

            var input = new EditText(context) { Hint = "Folder name" };
            input.SetTextColor(TitleColor(context));
            input.SetHintTextColor(SecondaryColor(context));
            card.AddView(input);

            var buttonRow = CreateButtonRow(context, Dp);

            var backBtn = CreateFlatButton(context, "Back", SecondaryColor(context));
            var createBtn = CreatePillButton(context, "Create", Dp);

            backBtn.Click += (s, e) => onBack();

            createBtn.Click += (s, e) =>
            {
                var name = input.Text?.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    showMessage("Enter a folder name");
                    return;
                }

                try
                {
                    var newPath = IOPath.Combine(parentPath, name);
                    Directory.CreateDirectory(newPath);
                    onCreated(newPath);
                }
                catch (Exception ex)
                {
                    showMessage($"Failed to create folder: {ex.Message}");
                }
            };

            var btnParams = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
            buttonRow.AddView(backBtn, btnParams);
            buttonRow.AddView(createBtn, btnParams);
            card.AddView(buttonRow);

            return card;
        }

        /// <summary>
        /// Builds a simple confirmation card (title, message, cancel + confirm buttons)
        /// styled identically to the folder picker cards.
        /// </summary>
        public static AndroidView BuildConfirmationCard(
            Context context,
            string title,
            string message,
            string confirmText,
            Action onConfirm,
            Action onCancel)
        {
            var density = context.Resources?.DisplayMetrics?.Density ?? 1f;
            int Dp(int dp) => (int)(dp * density);

            var card = CreateCard(context, Dp);

            card.AddView(CreateFolderTitle(context, title));

            var messageLabel = new TextView(context) { Text = message };
            messageLabel.SetTextColor(SecondaryColor(context));
            messageLabel.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 14);
            messageLabel.SetPadding(0, Dp(6), 0, Dp(6));
            card.AddView(messageLabel);

            var buttonRow = CreateButtonRow(context, Dp);

            var cancelBtn = CreateFlatButton(context, "Cancel", SecondaryColor(context));
            var confirmBtn = CreatePillButton(context, confirmText, Dp);

            cancelBtn.Click += (s, e) => onCancel();
            confirmBtn.Click += (s, e) => onConfirm();

            var btnParams = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
            buttonRow.AddView(cancelBtn, btnParams);
            buttonRow.AddView(confirmBtn, btnParams);
            card.AddView(buttonRow);

            return card;
        }

        /// <summary>
        /// Builds a small bottom snackbar-style card, e.g. "Moved to Pictures ✅".
        /// </summary>
        public static AndroidView BuildSnackbarCard(Context context, string message)
        {
            var density = context.Resources?.DisplayMetrics?.Density ?? 1f;
            int Dp(int dp) => (int)(dp * density);

            var bar = new LinearLayout(context) { Orientation = Orientation.Horizontal };
            var bg = new global::Android.Graphics.Drawables.GradientDrawable();
            bg.SetColor(Color.ParseColor("#323232"));
            bg.SetCornerRadius(Dp(24));
            bar.Background = bg;
            bar.SetPadding(Dp(20), Dp(12), Dp(20), Dp(12));
            bar.Elevation = Dp(6);
            bar.SetGravity(GravityFlags.CenterVertical);

            var label = new TextView(context) { Text = message };
            label.SetTextColor(Color.White);
            label.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 14);
            bar.AddView(label);

            return bar;
        }

        /// <summary>
        /// Builds the "Screenshot detected" card shown by the OverlayService.
        /// </summary>
        public static AndroidView BuildScreenshotDetectedCard(
            Context context,
            Action onSelectFolder,
            Action onCancel)
        {
            var density = context.Resources?.DisplayMetrics?.Density ?? 1f;
            int Dp(int dp) => (int)(dp * density);

            var card = CreateCard(context, Dp);

            var titleView = CreateTitle(context, "Screenshot detected!");
            titleView.Gravity = GravityFlags.CenterHorizontal;
            card.AddView(titleView);

            var messageLabel = new TextView(context) { Text = "Where would you like to save it?" };
            messageLabel.SetTextColor(SecondaryColor(context));
            messageLabel.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 14);
            messageLabel.SetPadding(0, Dp(6), 0, Dp(6));
            messageLabel.Gravity = GravityFlags.CenterHorizontal;
            card.AddView(messageLabel);

            var buttonRow = CreateButtonRow(context, Dp);

            var cancelBtn = CreateFlatButton(context, "Cancel", SecondaryColor(context));
            var selectBtn = CreatePillButton(context, "Select folder", Dp, withFolderIcon: true);

            cancelBtn.Click += (s, e) => onCancel();
            selectBtn.Click += (s, e) => onSelectFolder();

            var btnParams = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
            buttonRow.AddView(cancelBtn, btnParams);
            buttonRow.AddView(selectBtn, btnParams);
            card.AddView(buttonRow);

            return card;
        }

        /// <summary>
        /// Computes the preferred picker width for the current screen.
        /// </summary>
        public static int GetPreferredWidth(Context context)
        {
            int screenWidth = context.Resources?.DisplayMetrics?.WidthPixels ?? 1080;
            var density = context.Resources?.DisplayMetrics?.Density ?? 1f;
            var screenWidthDp = screenWidth / density;

            return screenWidthDp >= 600
                ? (int)(screenWidth * 0.5f)
                : (int)(screenWidth * 0.88f);
        }

        /// <summary>
        /// Adapter rendering each row with a theme-tinted folder icon and the
        /// folder name, keeping text visible regardless of the host theme.
        /// </summary>
        private sealed class FolderListAdapter : ArrayAdapter<string>
        {
            private readonly bool _canGoUp;

            public FolderListAdapter(Context context, IList<string> items, bool canGoUp)
                : base(context, global::Android.Resource.Layout.SimpleListItem1, items)
            {
                _canGoUp = canGoUp;
            }

            public override AndroidView GetView(int position, AndroidView? convertView, ViewGroup parent)
            {
                var density = Context.Resources?.DisplayMetrics?.Density ?? 1f;
                int Dp(int dp) => (int)(dp * density);

                LinearLayout row;
                ImageView icon;
                TextView label;

                if (convertView is LinearLayout existing && existing.ChildCount == 2)
                {
                    row = existing;
                    icon = (ImageView)existing.GetChildAt(0)!;
                    label = (TextView)existing.GetChildAt(1)!;
                }
                else
                {
                    row = new LinearLayout(Context) { Orientation = Orientation.Horizontal };
                    row.SetGravity(GravityFlags.CenterVertical);
                    row.SetPadding(Dp(8), Dp(10), Dp(8), Dp(10));

                    icon = new ImageView(Context);
                    row.AddView(icon, new LinearLayout.LayoutParams(Dp(22), Dp(22)));

                    label = new TextView(Context);
                    label.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 15);
                    var labelParams = new LinearLayout.LayoutParams(
                        ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
                    { MarginStart = Dp(12) };
                    row.AddView(label, labelParams);
                }

                label.SetTextColor(TitleColor(Context));

                bool isUpRow = _canGoUp && position == 0;
                if (isUpRow)
                {
                    var back = Context.GetDrawable(Resource.Drawable.ic_back)?.Mutate();
                    back?.SetTint(ThemeAccent(Context));
                    icon.Visibility = ViewStates.Visible;
                    icon.SetImageDrawable(back);
                    label.Text = "Back";
                }
                else
                {
                    icon.Visibility = ViewStates.Visible;
                    icon.SetImageDrawable(new FolderDrawable(ThemeAccent(Context)));
                    label.Text = GetItem(position);
                }

                return row;
            }
        }

        /// <summary>Canvas-drawn folder glyph so no emoji/OEM font variance.</summary>
        private sealed class FolderDrawable : global::Android.Graphics.Drawables.Drawable
        {
            private readonly AndroidPaint _paint;

            public FolderDrawable(Color color)
            {
                _paint = new AndroidPaint(PaintFlags.AntiAlias) { Color = color };
                _paint.SetStyle(AndroidPaint.Style.Fill);
            }

            public override void Draw(Canvas canvas)
            {
                var b = Bounds;
                float w = b.Width(), h = b.Height();
                float left = b.Left + w * 0.06f, right = b.Right - w * 0.06f;
                float top = b.Top + h * 0.16f, bottom = b.Bottom - h * 0.14f;
                float tabWidth = w * 0.40f, tabHeight = h * 0.16f;
                float radius = w * 0.10f;

                using var path = new global::Android.Graphics.Path();
                path.AddRoundRect(left, top, left + tabWidth, top + tabHeight * 2f, radius, radius, global::Android.Graphics.Path.Direction.Cw!);
                path.AddRoundRect(left, top + tabHeight, right, bottom, radius, radius, global::Android.Graphics.Path.Direction.Cw!);
                canvas.DrawPath(path, _paint);
            }

            public override void SetAlpha(int alpha) => _paint.Alpha = alpha;

            public override void SetColorFilter(ColorFilter? colorFilter) => _paint.SetColorFilter(colorFilter);

            public override int Opacity => (int)global::Android.Graphics.Format.Translucent;
        }

        private static LinearLayout CreateCard(Context context, Func<int, int> dp)
        {
            var card = new LinearLayout(context) { Orientation = Orientation.Vertical };
            var cardBg = new global::Android.Graphics.Drawables.GradientDrawable();
            cardBg.SetColor(CardColor(context));
            cardBg.SetCornerRadius(dp(20));
            card.Background = cardBg;
            card.SetPadding(dp(20), dp(20), dp(20), dp(12));
            card.Elevation = dp(8);
            return card;
        }

        private static TextView CreateTitle(Context context, string text)
        {
            var title = new TextView(context) { Text = text };
            title.SetTextColor(TitleColor(context));
            title.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 18);
            title.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
            return title;
        }

        private static LinearLayout CreateFolderTitle(Context context, string text)
        {
            var density = context.Resources?.DisplayMetrics?.Density ?? 1f;
            int Dp(int dp) => (int)(dp * density);

            var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
            row.SetGravity(GravityFlags.CenterVertical);

            var icon = new ImageView(context);
            icon.SetImageDrawable(new FolderDrawable(ThemeAccent(context)));
            row.AddView(icon, new LinearLayout.LayoutParams(Dp(22), Dp(22)));

            var title = CreateTitle(context, text);
            var titleParams = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
            { MarginStart = Dp(10) };
            row.AddView(title, titleParams);

            return row;
        }

        private static LinearLayout CreateButtonRow(Context context, Func<int, int> dp)
        {
            var buttonRow = new LinearLayout(context) { Orientation = Orientation.Horizontal };
            buttonRow.SetPadding(0, dp(10), 0, 0);
            return buttonRow;
        }

        private static AndroidButton CreateFlatButton(Context context, string text, Color textColor)
        {
            var btn = new AndroidButton(context) { Text = text };
            btn.SetTextColor(textColor);
            btn.SetBackgroundColor(Color.Transparent);
            btn.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 14);
            btn.SetAllCaps(false);
            return btn;
        }

        private static AndroidButton CreatePillButton(Context context, string text, Func<int, int> dp, bool withFolderIcon = false)
        {
            var btn = new AndroidButton(context) { Text = text };
            btn.SetTextColor(Color.White);
            btn.SetAllCaps(false);
            btn.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 14);
            var bg = new global::Android.Graphics.Drawables.GradientDrawable();
            bg.SetColor(Color.ParseColor(AccentColor));
            bg.SetCornerRadius(dp(20));
            btn.Background = bg;
            btn.SetPadding(dp(16), 0, dp(16), 0);

            if (withFolderIcon)
            {
                // Inline ImageSpan keeps the icon tight against the centered text
                var icon = new FolderDrawable(Color.White);
                icon.SetBounds(0, 0, dp(15), dp(15));

                var alignment = global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.Q
                    ? global::Android.Text.Style.SpanAlign.Center
                    : global::Android.Text.Style.SpanAlign.Baseline;

                var spannable = new global::Android.Text.SpannableString("x " + text);
                spannable.SetSpan(new global::Android.Text.Style.ImageSpan(icon, alignment),
                    0, 1, global::Android.Text.SpanTypes.ExclusiveExclusive);
                btn.TextFormatted = spannable;
            }

            return btn;
        }
    }
}
