using System.ComponentModel.DataAnnotations;

namespace FishingPlanner.Models;

/// <summary>
/// A rendszerbe regisztrált felhasználó profiladata (megjelenített névvel és profilképpel).
/// </summary>
public sealed record RegisztraltFelhasznalo(
    Guid Id,
    string Felhasznalonev,
    string? MegjelenitettNev = null,
    string? ProfilkepUrl = null)
{
    public string MegjelenitesiNev =>
        !string.IsNullOrWhiteSpace(MegjelenitettNev) ? MegjelenitettNev.Trim() : Felhasznalonev;
}

/// <summary>
/// A horgásztúrán részt vevő regisztrált felhasználót reprezentáló entitás.
/// </summary>
public class Resztvevo : IValidatableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required(ErrorMessage = "A túra azonosítója kötelező.")]
    public Guid TuraId { get; set; }

    /// <summary>
    /// A kiválasztott regisztrált felhasználó (profiles.id) azonosítója.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// A kiválasztott regisztrált felhasználó neve (automatikusan töltődik a profilból).
    /// </summary>
    [Required(ErrorMessage = "A résztvevő neve kötelező.")]
    [StringLength(80, MinimumLength = 2, ErrorMessage = "A résztvevő neve 2 és 80 karakter között legyen.")]
    public string Nev { get; set; } = string.Empty;

    [Required(ErrorMessage = "A szerepkör megadása kötelező.")]
    [StringLength(60, ErrorMessage = "A szerepkör legfeljebb 60 karakter lehet.")]
    public string Szerepkor { get; set; } = "Horgász";

    /// <summary>
    /// Igaz, ha a résztvevő jelenleg aktív tagja a túrának; hamis, ha eltávolították (a fogásai és felszerelései megmaradnak).
    /// </summary>
    public bool AktivE { get; set; } = true;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (TuraId == Guid.Empty)
        {
            yield return new ValidationResult(
                "Érvényes túra kiválasztása kötelező.",
                [nameof(TuraId)]);
        }
    }
}
