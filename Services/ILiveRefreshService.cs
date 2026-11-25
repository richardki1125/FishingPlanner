namespace FishingPlanner.Services;

/// <summary>
/// Az aktív oldal élő, automatikus háttérfrissítését és a kézi (pull-to-refresh) frissítését koordináló szolgáltatás.
/// </summary>
public interface ILiveRefreshService
{
    bool IsRefreshing { get; }

    event Action? RefreshStateChanged;

    /// <summary>
    /// Regisztrálja az aktuálisan megnyitott oldal frissítési műveletét.
    /// A paraméter (isManual) jelzi, hogy kézi lehúzás/gombnyomás (true) vagy automatikus háttérfrissítés (false) történt.
    /// </summary>
    IDisposable RegisterRefreshHandler(Func<bool, CancellationToken, Task> handler);

    /// <summary>
    /// Azonnali kézi frissítés (pl. lehúzásos pull-to-refresh vagy frissítés gomb).
    /// </summary>
    Task TriggerManualRefreshAsync(CancellationToken ct = default);

    /// <summary>
    /// Jelzi, hogy az alkalmazás előtérben van-e (háttérben szünetelteti a lekérdezéseket).
    /// </summary>
    void SetAppVisibility(bool isVisible);
}
