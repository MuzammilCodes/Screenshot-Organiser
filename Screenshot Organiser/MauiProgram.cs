using CommunityToolkit.Maui;
using Microsoft.Maui.LifecycleEvents;

namespace Screenshot_Organiser;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Configure lifecycle events for permission handling
        builder.ConfigureLifecycleEvents(events =>
        {
#if ANDROID
            events.AddAndroid(android => android
                .OnResume(activity =>
                {
                    // Notify MainPage that app has resumed
                    if (Application.Current?.MainPage is AppShell shell &&
                        shell.CurrentPage is MainPage mainPage)
                    {
                        mainPage.OnAppResumed();
                    }
                    else if (Application.Current?.MainPage is MainPage directMainPage)
                    {
                        directMainPage.OnAppResumed();
                    }
                }));
#endif
        });

        return builder.Build();
    }
}