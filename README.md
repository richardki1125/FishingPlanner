# 🎣 FishingPlanner

Többnapos közös horgásztúrák szervezésére, költségtervezésére, csomaglistáinak kezelésére és a fogások dokumentálására készült cross-platform **.NET 10 MAUI Blazor Hybrid** alkalmazás (**Android** és **Windows**).

A projekt modern **MVVM** architektúrára, **Supabase** (PostgreSQL + Auth + Storage + RLS) felhő háttérre és **Offline-First** helyi szinkronizációra épül, így térerő nélküli vízparti környezetben is megbízhatóan használható.

---

## Főbb funkciók

### Tervezés
- **Horgásztúrák kezelése (`/`)**:
  - Új túrák létrehozása, szerkesztése, törlése, dátum- és állapot szerinti szűrés (*Aktuális / Közelgő / Lejárt*) és rendezés.
  - Regisztrált horgásztársak meghívása szerepkörrel; adatmegőrző eltávolítás (*soft-delete*), így egy eltávolított majd visszavett résztvevő minden korábbi fogása és felszerelése megmarad.
  - Túra létrehozásakor és szerkesztésekor egyszerre több előre létrehozott csomaglista (sablon) is hozzáadható.
- **Csomaglista & Újrahasználható sablonok (`/csomaglista`)**:
  - Közös és egyéni felszerelések nyilvántartása, kategória és személy szerinti szűrés, bepakoltsági állapot pipálása élő százalékos kijelzővel.
  - **Csomaglistáim (sablonok)**: tetszőleges számú újrahasználható csomaglista készítése (pl. *Bojlis alapszett*, *Konyha*, *Csónakos*), meglévő túra listájának sablonként mentése, illetve több sablon hozzáadása bármely túrához.
- **Költségek & Elszámolás (`/koltsegek`)**:
  - Kategória alapú költségmezők felvétele (pl. *Ételek*, *Italok*, *Etetőanyag és csali*, *Üzemanyag*, *Horgászjegy*, *Szállás* vagy saját megnevezés).
  - **Közös** (aktív résztvevők között egyenlően elosztva) és **Egyéni** költségek kezelése, választható pénznemmel (`Ft`, `EUR`, `RON`).
  - Automatikus **Túra összköltség**, **Közös költség / fő**, **Személyenkénti leosztás**, valamint befizető megadása esetén minimális átutalásszámú **Elszámolási javaslat**.

### Vízparton
- **Fogási napló (`/fogasok`)**:
  - Súly + hossz alapú és darabszám alapú (pl. keszeg/kárász) fogások rögzítése, **Visszaengedett / Megtartott** jelöléssel.
  - Adatbázisból betöltött két szintű halfaj-katalógus (*Főkategória → Alkategória*, pl. *Ponty → Tükörponty*) + egyedi halfaj megadási lehetőség.
  - Fotó feltöltése kameráról vagy galériából (tömörített Supabase Storage feltöltéssel), teljes képernyős galéria-nézegető (*lightbox*) nagyítással és lapozással.
  - Horgász profilkép megjelenítése a fogáskártyán, rendezés (*Legújabb elöl*, *Legnagyobb elöl*, *Dátum növekvő*) és gyors szűrők.
- **Statisztika & Ranglista + Év végi összegzés (`/statisztika`)**:
  - **Túra statisztika**: Legnagyobb kifogott hal kiemelve elsőként, legeredményesebb csali és legaktívabb napszak külön **összsúly (kg)** és **darabszám (db)** alapján, túra összköltsége, visszaengedett és **személyekre bontott megtartott halak**, horgász ranglista, csalik, napszakok és 2 szintű halfaj-bontás.
  - **Év végi összegzés**: tetszőleges év összes túrájának összesítése (összes túra, vízterületek, túranapok, éves összköltség, az év legnagyobb hala, összevont éves horgász ranglista és túránkénti kattintható bontás).
- **Profil & Horgásznaptár (`/profil`)**:
  - Egyedi megjelenített név és profilkép feltöltése.
  - Interaktív **Horgásznaptár** színezett túranapokkal és **Eltöltött napok a vízparton** összesítővel minden regisztrált felhasználóhoz.

### Felhasználói élmény & Adatbiztonság
- **Offline-First működés**: helyi JSON tükör és szinkronizációs sor (`OfflineSyncTuraService`) — internet nélkül rögzített adatok és képek automatikusan feltöltődnek újracsatlakozáskor.
- **Élő frissítés & Pull-to-Refresh**: periodikus háttérfrissítés és mobilos lehúzásos frissítés.
- **Natív Android támogatás**: Android 13–16 Edge-to-Edge safe-area kezelés és alkalmazáson belüli Vissza gomb / back-gesture navigáció.
- **Világos / Sötét téma** (szemkímélő palettával, tiszta `#000000` fekete nélkül).
- **Adminisztrátori Adatbiztonság & Mentés (`/beallitasok`)**: teljes adatbázis JSON export / import és kézi felhőszinkronizálás (csak admin jogosultsággal látható).

---

## Technológiai stack

- **Keretrendszer**: .NET 10, .NET MAUI Blazor Hybrid (C#, Razor Components)
- **Architektúra**: MVVM (`ViewModelBase` + ViewModel-ek), Dependency Injection (`MauiProgram.cs`), `async/await` + `System.Text.Json` Source Generation (Android Release AOT/trimming kompatibilis)
- **Backend & Auth**: Supabase (PostgreSQL PostgREST, Supabase Auth felhasználónév+jelszó alapon, Supabase Storage, Row Level Security)
- **Célplatformok**: Android (`net10.0-android`, API 24+), Windows (`net10.0-windows10.0.19041.0`)

---

## Projekt felépítése

```text
FishingPlanner/
├── Components/
│   ├── Layout/           # MainLayout, NavMenu (oldalsáv + mobil alsó navigáció)
│   ├── Pages/            # Turak, Csomaglista, Koltsegek, FogasiNaplo, Statisztika, Profil, Beallitasok, Bejelentkezes
│   └── Shared/           # AlertBanner, ConfirmDialog, EmptyState, LoadingSpinner
├── Models/               # Domain entitások, validációk és statisztikai rekordok
├── Platforms/            # Android (MainActivity, Safe-Area, BackDispatcher) és Windows specifikus kód
├── Resources/
│   └── Raw/
│       └── supabase.config.example.json   # Konfigurációs sablon
├── Services/             # ITuraService, SupabaseTuraService, OfflineSyncTuraService, Auth, Media, Backup, Statisztika, LiveRefresh, BackNavigation
├── ViewModels/           # MVVM ViewModel réteg
├── supabase/
│   ├── schema.sql        # Teljes, idempotens PostgreSQL séma + RLS + Storage + alap halfaj-katalógus
│   └── add_*.sql         # Külön futtatható, 100%-ban adatmegőrző migrációs szkriptek
├── wwwroot/              # app.css (Light/Dark design system) és index.html (téma + Pull-to-Refresh)
└── MauiProgram.cs        # DI regisztrációk
```

---

## Telepítés és beállítás

### 1. Előfeltételek
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- MAUI workload telepítése:
  ```bash
  dotnet workload install maui
  ```
- Egy ingyenes [Supabase](https://supabase.com/) projekt.

### 2. Supabase adatbázis létrehozása
1. Nyisd meg a Supabase Dashboard → **SQL Editor** felületet.
2. Futtasd le a [`supabase/schema.sql`](supabase/schema.sql) fájl teljes tartalmát (idempotens, létrehozza a táblákat, indexeket, RLS szabályokat, a `fogas-kepek` Storage bucketet és az alap halfaj-katalógust).
3. *(Opcionális)* A **Authentication → Providers → Email** beállításnál kapcsold ki a *Confirm email* opciót, ha azonnali, felhasználónév + jelszó alapú regisztrációt szeretnél.

### 3. Helyi konfiguráció (titkos kulcsok védelme)
Másold le a sablonfájlt és add meg a saját Supabase projekted adatait:
```bash
cp Resources/Raw/supabase.config.example.json Resources/Raw/supabase.config.json
```
A `Resources/Raw/supabase.config.json` tartalma:
```json
{
  "SupabaseUrl": "https://SAJAT_PROJEKT_ID.supabase.co",
  "AnonKey": "SAJAT_SUPABASE_ANON_KEY"
}
```
> **Fontos:** A `Resources/Raw/supabase.config.json` szerepel a `.gitignore` fájlban, így a valódi kulcsok soha nem kerülnek fel GitHubra.

### 4. Futtatás és Android APK készítése
- **Windows Release build**:
  ```bash
  dotnet build -f net10.0-windows10.0.19041.0 -c Release
  ```
- **Önálló Android APK készítése**:
  ```bash
  dotnet build -f net10.0-android -c Release
  ```
  Az elkészült telepíthető APK elérési útja:
  `bin/Release/net10.0-android/com.companyname.fishingplanner-Signed.apk`
