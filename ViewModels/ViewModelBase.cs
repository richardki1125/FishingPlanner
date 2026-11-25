using Microsoft.Extensions.Logging;

namespace FishingPlanner.ViewModels;

/// <summary>
/// Közös MVVM alaposztály az állapotkezeléshez, töltésjelzéshez és egységes hibakezeléshez.
/// </summary>
public abstract class ViewModelBase
{
    protected ILogger Logger { get; }

    public bool IsLoading { get; protected set; }
    public bool IsBusy { get; protected set; }
    public string? ErrorMessage { get; protected set; }
    public string? SuccessMessage { get; protected set; }

    public event Action? StateChanged;

    protected ViewModelBase(ILogger logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void ClearMessages()
    {
        ErrorMessage = null;
        SuccessMessage = null;
        NotifyStateChanged();
    }

    protected void SetError(string message)
    {
        ErrorMessage = message;
        SuccessMessage = null;
        NotifyStateChanged();
    }

    protected void SetSuccess(string message)
    {
        SuccessMessage = message;
        ErrorMessage = null;
        NotifyStateChanged();
    }

    protected void NotifyStateChanged() => StateChanged?.Invoke();

    /// <summary>
    /// Egységes aszinkron műveletvégrehajtás töltésjelzővel és try-catch hibakezeléssel.
    /// </summary>
    protected async Task<bool> ExecuteAsync(
        Func<Task> action,
        string fallbackErrorMessage,
        bool useFullPageLoading = false)
    {
        ErrorMessage = null;
        if (useFullPageLoading)
        {
            IsLoading = true;
        }
        else
        {
            IsBusy = true;
        }

        NotifyStateChanged();

        try
        {
            await action();
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (InvalidOperationException ex)
        {
            Logger.LogWarning(ex, "{FallbackMessage}", fallbackErrorMessage);
            ErrorMessage = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "{FallbackMessage}", fallbackErrorMessage);
            ErrorMessage = $"{fallbackErrorMessage} ({ex.Message})";
            return false;
        }
        finally
        {
            if (useFullPageLoading)
            {
                IsLoading = false;
            }
            else
            {
                IsBusy = false;
            }

            NotifyStateChanged();
        }
    }
}
