using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Activity;
using AndroidX.Core.View;
using FishingPlanner.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FishingPlanner;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private AppBackPressedCallback? _backCallback;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Android 11–16 rendszer Vissza gomb és gesztus kezelése az alkalmazáson belül
        _backCallback = new AppBackPressedCallback(this);
        OnBackPressedDispatcher.AddCallback(this, _backCallback);

        // Android 15/16 Edge-to-Edge védelem: a WebView ne csússzon be az óra/állapotsáv vagy az alsó navigációs sáv alá
        if (Window is not null)
        {
            WindowCompat.SetDecorFitsSystemWindows(Window, false);

            var decorView = Window.DecorView;
            decorView.SetBackgroundColor(Android.Graphics.Color.ParseColor("#141A1C"));

            var insetsController = WindowCompat.GetInsetsController(Window, decorView);
            if (insetsController is not null)
            {
                insetsController.AppearanceLightStatusBars = false;
                insetsController.AppearanceLightNavigationBars = false;
            }

            var contentView = decorView.FindViewById(Android.Resource.Id.Content);
            if (contentView is not null)
            {
                contentView.SetBackgroundColor(Android.Graphics.Color.ParseColor("#141A1C"));
                ViewCompat.SetOnApplyWindowInsetsListener(contentView, new SystemBarInsetsListener());
                ViewCompat.RequestApplyInsets(contentView);
            }
        }
    }

    private sealed class AppBackPressedCallback(MainActivity activity) : OnBackPressedCallback(true)
    {
        public override void HandleOnBackPressed()
        {
            var backService = IPlatformApplication.Current?.Services.GetService<IBackNavigationService>();
            if (backService is not null && backService.HandleBackPressed())
            {
                return;
            }

            // Ha már a főoldalon vagyunk nyitott ablak nélkül, normál Android visszalépés / kilépés
            Enabled = false;
            try
            {
                activity.OnBackPressedDispatcher.OnBackPressed();
            }
            finally
            {
                Enabled = true;
            }
        }
    }

    private sealed class SystemBarInsetsListener : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat? OnApplyWindowInsets(Android.Views.View? v, WindowInsetsCompat? insets)
        {
            if (v is not null && insets is not null)
            {
                var barInsets = insets.GetInsets(
                    WindowInsetsCompat.Type.SystemBars() |
                    WindowInsetsCompat.Type.DisplayCutout() |
                    WindowInsetsCompat.Type.Ime());

                if (barInsets is not null)
                {
                    v.SetPadding(barInsets.Left, barInsets.Top, barInsets.Right, barInsets.Bottom);
                }
            }

            return insets;
        }
    }
}
