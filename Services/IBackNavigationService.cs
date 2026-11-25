namespace FishingPlanner.Services;

/// <summary>
/// Az Android rendszer Vissza gombjának és gesztusának alkalmazáson belüli kezelését koordináló szolgáltatás.
/// Először a megnyitott felugró ablakokat / képnézegetőt zárja be, majd visszalép az előző oldalra.
/// </summary>
public interface IBackNavigationService
{
    /// <summary>
    /// Regisztrál egy oldalszintű (modal / képnézegető / dialógus) vissza-kezelőt.
    /// Akkor térjen vissza true-val, ha bezárt egy nyitott ablakot.
    /// </summary>
    IDisposable RegisterOverlayHandler(Func<bool> tryCloseOverlay);

    /// <summary>
    /// Regisztrálja a fő (oldalak közötti) visszalépés kezelőjét a MainLayout-ból.
    /// </summary>
    IDisposable RegisterNavigationHandler(Func<bool> tryNavigateBack);

    /// <summary>
    /// A natív Android Vissza gomb hívja meg.
    /// True = az app feldolgozta (nem lép ki); False = a főoldalon vagyunk, kiléphet az app.
    /// </summary>
    bool HandleBackPressed();
}
