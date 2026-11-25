namespace FishingPlanner.Services;

/// <summary>
/// A jelenleg kiválasztott aktív túra azonosítójának megosztása az oldalak között.
/// </summary>
public interface IActiveTuraState
{
    Guid? SelectedTuraId { get; }
    event Action<Guid?>? SelectedTuraChanged;
    void SetSelectedTura(Guid? turaId);
}

public sealed class ActiveTuraState : IActiveTuraState
{
    public Guid? SelectedTuraId { get; private set; }

    public event Action<Guid?>? SelectedTuraChanged;

    public void SetSelectedTura(Guid? turaId)
    {
        if (SelectedTuraId == turaId)
        {
            return;
        }

        SelectedTuraId = turaId;
        SelectedTuraChanged?.Invoke(turaId);
    }
}
