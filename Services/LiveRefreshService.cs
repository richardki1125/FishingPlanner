using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace FishingPlanner.Services;

/// <summary>
/// Automatikus (10 másodperces) élő háttérfrissítést és kézi lehúzásos frissítést vezérlő singleton szolgáltatás.
/// Csak bejelentkezett és előtérben lévő alkalmazás esetén futtat háttérfrissítést, ütközésvédelemmel.
/// </summary>
public sealed class LiveRefreshService : ILiveRefreshService, IDisposable
{
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MinManualVisualDuration = TimeSpan.FromMilliseconds(350);

    private readonly IAuthService _authService;
    private readonly ILogger<LiveRefreshService> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _handlerLock = new();
    private readonly CancellationTokenSource _cts = new();

    private Func<bool, CancellationToken, Task>? _currentHandler;
    private volatile bool _isAppVisible = true;
    private bool _isRefreshing;

    public bool IsRefreshing => _isRefreshing;
    public event Action? RefreshStateChanged;

    public LiveRefreshService(IAuthService authService, ILogger<LiveRefreshService> logger)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _ = RunAutoRefreshLoopAsync(_cts.Token);
    }

    public IDisposable RegisterRefreshHandler(Func<bool, CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_handlerLock)
        {
            _currentHandler = handler;
        }

        return new HandlerRegistration(this, handler);
    }

    public async Task TriggerManualRefreshAsync(CancellationToken ct = default)
    {
        if (!_authService.IsAuthenticated)
        {
            return;
        }

        var sw = Stopwatch.StartNew();
        SetRefreshingState(true);

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, ct);
            await ExecuteRefreshAsync(isManual: true, linkedCts.Token);

            var remaining = MinManualVisualDuration - sw.Elapsed;
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, linkedCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Megszakított frissítés normál működés
        }
        finally
        {
            SetRefreshingState(false);
        }
    }

    public void SetAppVisibility(bool isVisible)
    {
        var wasHidden = !_isAppVisible;
        _isAppVisible = isVisible;

        if (wasHidden && isVisible && _authService.IsAuthenticated)
        {
            _ = ExecuteRefreshAsync(isManual: false, _cts.Token);
        }
    }

    private async Task RunAutoRefreshLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(AutoRefreshInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (!_isAppVisible || !_authService.IsAuthenticated)
                {
                    continue;
                }

                await ExecuteRefreshAsync(isManual: false, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Szolgáltatás leállítva
        }
    }

    private async Task ExecuteRefreshAsync(bool isManual, CancellationToken ct)
    {
        Func<bool, CancellationToken, Task>? handler;
        lock (_handlerLock)
        {
            handler = _currentHandler;
        }

        if (handler is null)
        {
            return;
        }

        var acquired = isManual
            ? await WaitGateAsync(ct)
            : await _refreshGate.WaitAsync(0, ct);

        if (!acquired)
        {
            return;
        }

        try
        {
            await handler(isManual, ct);
        }
        catch (OperationCanceledException)
        {
            // Normál megszakítás
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hiba történt az oldal élő frissítése közben (isManual={IsManual}).", isManual);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<bool> WaitGateAsync(CancellationToken ct)
    {
        await _refreshGate.WaitAsync(ct);
        return true;
    }

    private void Unregister(Func<bool, CancellationToken, Task> handler)
    {
        lock (_handlerLock)
        {
            if (ReferenceEquals(_currentHandler, handler))
            {
                _currentHandler = null;
            }
        }
    }

    private void SetRefreshingState(bool refreshing)
    {
        if (_isRefreshing == refreshing)
        {
            return;
        }

        _isRefreshing = refreshing;
        RefreshStateChanged?.Invoke();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _refreshGate.Dispose();
    }

    private sealed class HandlerRegistration(LiveRefreshService owner, Func<bool, CancellationToken, Task> handler) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner.Unregister(handler);
        }
    }
}
