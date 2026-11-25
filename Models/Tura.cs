using System.ComponentModel.DataAnnotations;

namespace FishingPlanner.Models;

/// <summary>
/// Egy horgásztúra alapadatait reprezentáló entitás kezdő és befejező dátummal.
/// </summary>
public class Tura : IValidatableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required(ErrorMessage = "A túra neve kötelező.")]
    [StringLength(100, MinimumLength = 3, ErrorMessage = "A túra neve 3 és 100 karakter között legyen.")]
    public string Nev { get; set; } = string.Empty;

    [Required(ErrorMessage = "A helyszín megadása kötelező.")]
    [StringLength(150, MinimumLength = 2, ErrorMessage = "A helyszín 2 és 150 karakter között legyen.")]
    public string Helyszin { get; set; } = string.Empty;

    [Required(ErrorMessage = "A kezdő dátum megadása kötelező.")]
    public DateTime KezdoDatum { get; set; } = DateTime.Today;

    [Required(ErrorMessage = "A befejező dátum megadása kötelező.")]
    public DateTime VegDatum { get; set; } = DateTime.Today.AddDays(6);

    /// <summary>
    /// A túra hossza napokban (kezdő és záró nappal együtt).
    /// </summary>
    public int NapokSzama => Math.Max(1, (VegDatum.Date - KezdoDatum.Date).Days + 1);

    /// <summary>
    /// Igaz, ha a túra befejező dátuma már a múltban van.
    /// </summary>
    public bool LejartE => VegDatum.Date < DateTime.Today;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (VegDatum.Date < KezdoDatum.Date)
        {
            yield return new ValidationResult(
                "A befejező dátum nem lehet korábbi a kezdő dátumnál.",
                [nameof(VegDatum)]);
        }

        var napok = (VegDatum.Date - KezdoDatum.Date).Days + 1;
        if (napok > 30)
        {
            yield return new ValidationResult(
                "Egy túra legfeljebb 30 napos lehet.",
                [nameof(VegDatum)]);
        }
    }
}
