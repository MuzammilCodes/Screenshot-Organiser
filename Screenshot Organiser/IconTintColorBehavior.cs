using Android.Graphics;
using Android.Widget;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform;
using MauiColor = Microsoft.Maui.Graphics.Color;

namespace Screenshot_Organiser;

public class IconTintColorBehavior : Behavior<Image>
{
    private Image? _associatedObject;

    public static readonly BindableProperty TintColorProperty =
        BindableProperty.Create(nameof(TintColor), typeof(MauiColor), typeof(IconTintColorBehavior), Colors.Transparent,
            propertyChanged: OnTintColorChanged);

    public MauiColor TintColor
    {
        get => (MauiColor)GetValue(TintColorProperty);
        set => SetValue(TintColorProperty, value);
    }

    protected override void OnAttachedTo(Image bindable)
    {
        base.OnAttachedTo(bindable);
        _associatedObject = bindable;
        bindable.HandlerChanged += OnHandlerChanged;
        UpdateTint();
    }

    protected override void OnDetachingFrom(Image bindable)
    {
        bindable.HandlerChanged -= OnHandlerChanged;
        _associatedObject = null;
        ClearTint();
        base.OnDetachingFrom(bindable);
    }

    private void OnHandlerChanged(object? sender, EventArgs e)
    {
        UpdateTint();
    }

    private static void OnTintColorChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is IconTintColorBehavior behavior)
        {
            behavior.UpdateTint();
        }
    }

    private void UpdateTint()
    {
        if (_associatedObject is null)
        {
            return;
        }

        if (TintColor == Colors.Transparent)
        {
            ClearTint();
            return;
        }

#if ANDROID
        var platformView = _associatedObject.Handler?.PlatformView as ImageView;
        if (platformView is null)
        {
            return;
        }

        platformView.ClearColorFilter();
        platformView.SetColorFilter(TintColor.ToPlatform(), PorterDuff.Mode.SrcIn);
#endif
    }

    private void ClearTint()
    {
#if ANDROID
        if (_associatedObject?.Handler?.PlatformView is ImageView imageView)
        {
            imageView.ClearColorFilter();
        }
#endif
    }
}
