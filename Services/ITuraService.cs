using FishingPlanner.Models;

namespace FishingPlanner.Services;

/// <summary>
/// A horgásztúrák, résztvevők, felszerelések és fogások kezeléséért felelős adatelérési szerződés.
/// </summary>
public interface ITuraService
{
    // Túrák
    Task<IReadOnlyList<Tura>> GetTurakAsync(CancellationToken ct = default);
    Task<Tura?> GetTuraByIdAsync(Guid turaId, CancellationToken ct = default);
    Task<Tura> CreateTuraAsync(Tura tura, CancellationToken ct = default);
    Task<Tura> UpdateTuraAsync(Tura tura, CancellationToken ct = default);
    Task DeleteTuraAsync(Guid turaId, CancellationToken ct = default);

    // Regisztrált felhasználók (résztvevő-választó listához)
    Task<IReadOnlyList<RegisztraltFelhasznalo>> GetRegisztraltFelhasznalokAsync(CancellationToken ct = default);

    // Hal főkategóriák és alkategóriák (adatbázisból töltött 2 szintű halfaj-katalógus)
    Task<IReadOnlyList<HalKategoriaCsoport>> GetHalKategoriakAsync(CancellationToken ct = default);

    // Résztvevők
    Task<IReadOnlyList<Resztvevo>> GetResztvevokAsync(Guid turaId, CancellationToken ct = default);
    Task<Resztvevo> AddResztvevoAsync(Resztvevo resztvevo, CancellationToken ct = default);
    Task<Resztvevo> UpdateResztvevoAsync(Resztvevo resztvevo, CancellationToken ct = default);
    Task DeleteResztvevoAsync(Guid resztvevoId, CancellationToken ct = default);

    // Felszerelések
    Task<IReadOnlyList<FelszerelesTetel>> GetFelszerelesekAsync(Guid turaId, CancellationToken ct = default);
    Task<FelszerelesTetel> AddFelszerelesAsync(FelszerelesTetel tetel, CancellationToken ct = default);
    Task<FelszerelesTetel> UpdateFelszerelesAsync(FelszerelesTetel tetel, CancellationToken ct = default);
    Task ToggleBepakolvaAsync(Guid tetelId, bool bepakolva, CancellationToken ct = default);
    Task DeleteFelszerelesAsync(Guid tetelId, CancellationToken ct = default);

    // Fogások
    Task<IReadOnlyList<Fogas>> GetFogasokAsync(Guid turaId, CancellationToken ct = default);
    Task<Fogas> AddFogasAsync(Fogas fogas, CancellationToken ct = default);
    Task<Fogas> UpdateFogasAsync(Fogas fogas, CancellationToken ct = default);
    Task DeleteFogasAsync(Guid fogasId, CancellationToken ct = default);

    // Költségek
    Task<IReadOnlyList<KoltsegTetel>> GetKoltsegekAsync(Guid turaId, CancellationToken ct = default);
    Task<KoltsegTetel> AddKoltsegAsync(KoltsegTetel tetel, CancellationToken ct = default);
    Task<KoltsegTetel> UpdateKoltsegAsync(KoltsegTetel tetel, CancellationToken ct = default);
    Task DeleteKoltsegAsync(Guid koltsegId, CancellationToken ct = default);

    // Újrahasználható csomaglisták (sablonok)
    Task<IReadOnlyList<CsomaglistaSablon>> GetCsomaglistaSablonokAsync(CancellationToken ct = default);
    Task<CsomaglistaSablon> CreateCsomaglistaSablonAsync(CsomaglistaSablon sablon, CancellationToken ct = default);
    Task<CsomaglistaSablon> UpdateCsomaglistaSablonAsync(CsomaglistaSablon sablon, CancellationToken ct = default);
    Task DeleteCsomaglistaSablonAsync(Guid sablonId, CancellationToken ct = default);

    Task<IReadOnlyList<CsomaglistaSablonTetel>> GetCsomaglistaSablonTetelekAsync(Guid sablonId, CancellationToken ct = default);
    Task<IReadOnlyList<CsomaglistaSablonTetel>> GetAllCsomaglistaSablonTetelekAsync(CancellationToken ct = default);
    Task<CsomaglistaSablonTetel> AddCsomaglistaSablonTetelAsync(CsomaglistaSablonTetel tetel, CancellationToken ct = default);
    Task<CsomaglistaSablonTetel> UpdateCsomaglistaSablonTetelAsync(CsomaglistaSablonTetel tetel, CancellationToken ct = default);
    Task DeleteCsomaglistaSablonTetelAsync(Guid tetelId, CancellationToken ct = default);
}
