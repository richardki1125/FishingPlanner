using Microsoft.Extensions.Logging;

namespace FishingPlanner.Services;

/// <summary>
/// Szálbiztos, LIFO sorrendű vissza-navigáció kezelő:
/// 1. Nyitott képnézegető / megerősítő ablak / űrlap modal bezárása
/// 2. Visszalépés az alkalmazás korábbi oldalára (vagy a főoldalra)
/// 3. Csak a főoldalon, nyitott ablak nélkül engedi az Android kilépést.
/// </summary>
public sealed class BackNavigationService : IBackNavigationService
{
    private readonly ILogger<BackNavigationService> _logger;
    private readonly object _lock = new();
    private readonly List<Func<bool>> _overlayHandlers = [];
    private Func<bool>? _navigationHandler;

    public BackNavigationService(ILogger<BackNavigationService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IDisposable RegisterOverlayHandler(Func<bool> tryCloseOverlay)
    {
        ArgumentNullException.ThrowIfNull(tryCloseOverlay);

        lock (_lock)
        {
            _overlayHandlers.Add(tryCloseOverlay);
        }

        return new Registration(() =>
        {
            lock (_lock)
            {
                _overlayHandlers.Remove(tryCloseOverlay);
            }
        });
    }

    public IDisposable RegisterNavigationHandler(Func<bool> tryNavigateBack)
    {
        ArgumentNullException.ThrowIfNull(tryNavigateBack);

        lock (_lock)
        {
            _navigationHandler = tryNavigateBack;
        }

        return new Registration(() =>
        {
            lock (_lock)
            {
                if (ReferenceEquals(_navigationHandler, tryNavigateBack))
                {
                    _navigationHandler = null;
                }
            }
        });
    }

    public bool HandleBackPressed()
    {
        List<Func<bool>> overlaysSnapshot;
        Func<bool>? navHandler;

        lock (_lock)
        {
            overlaysSnapshot = [.. _overlayHandlers];
            navHandler = _navigationHandler;
        }

        // 1. Először a legfelső megnyitott modal / lightbox / megerősítő ablak bezárása
        for (var i = overlaysSnapshot.Count - 1; i >= 0; i--)
        {
            try
            {
                if (overlaysSnapshot[i]())
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Hiba történt az overlay vissza-kezelő futtatásakor.");
            }
        }

        // 2. Ha nincs nyitott ablak, visszalépés az alkalmazáson belüli előzményekben
        if (navHandler is not null)
        {
            try
            {
                if (navHandler())
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Hiba történt az oldal-visszalépés végrehajtásakor.");
            }
        }

        return false;
    }

    private sealed class Registration(Action onDispose) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            onDispose();
        }
    }
}
