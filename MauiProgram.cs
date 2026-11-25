using FishingPlanner.Services;
using FishingPlanner.ViewModels;
using Microsoft.Extensions.Logging;

namespace FishingPlanner;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        // Hálózati kliens és helyi szolgáltatások
        builder.Services.AddSingleton<HttpClient>();
        builder.Services.AddSingleton<ISettingsService, SettingsService>();
        builder.Services.AddSingleton<IActiveTuraState, ActiveTuraState>();
        builder.Services.AddSingleton<IStatisztikaService, StatisztikaService>();

        // Supabase konfiguráció, hitelesítés, tömörített képfeltöltés és Offline-First adatelérés
        builder.Services.AddSingleton<ISupabaseConfigService, SupabaseConfigService>();
        builder.Services.AddSingleton<IAuthService, SupabaseAuthService>();
        builder.Services.AddSingleton<ILiveRefreshService, LiveRefreshService>();
        builder.Services.AddSingleton<IBackNavigationService, BackNavigationService>();
        builder.Services.AddSingleton<IMediaService, MauiMediaService>();

        builder.Services.AddSingleton<SupabaseTuraService>();
        builder.Services.AddSingleton<OfflineSyncTuraService>();
        builder.Services.AddSingleton<ITuraService>(sp => sp.GetRequiredService<OfflineSyncTuraService>());
        builder.Services.AddSingleton<ISyncStatusService>(sp => sp.GetRequiredService<OfflineSyncTuraService>());
        builder.Services.AddSingleton<IBackupService, BackupService>();

        // MVVM ViewModel-ek
        builder.Services.AddTransient<AuthViewModel>();
        builder.Services.AddTransient<TuraListaViewModel>();
        builder.Services.AddTransient<CsomaglistaViewModel>();
        builder.Services.AddTransient<KoltsegekViewModel>();
        builder.Services.AddTransient<FogasiNaploViewModel>();
        builder.Services.AddTransient<StatisztikaViewModel>();
        builder.Services.AddTransient<ProfilViewModel>();
        builder.Services.AddTransient<BeallitasokViewModel>();

        return builder.Build();
    }
}
