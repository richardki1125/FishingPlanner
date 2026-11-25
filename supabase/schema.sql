-- =============================================================================
-- FishingPlanner - Supabase (PostgreSQL + Auth + RLS + Storage) séma
-- Felhasználónév + jelszó alapú regisztráció, regisztrált felhasználókból
-- választható résztvevők, súlylimit nélküli túratervezés.
-- Futtasd le a Supabase Dashboard -> SQL Editor felületén!
-- Az SQL szkript idempotens (többször is biztonságosan futtatható).
-- =============================================================================

create extension if not exists "pgcrypto";

-- 1. Felhasználói profilok (összekötve a Supabase Auth auth.users táblával)
create table if not exists public.profiles (
    id uuid primary key references auth.users(id) on delete cascade,
    felhasznalonev text not null check (char_length(trim(felhasznalonev)) between 3 and 30),
    megjelenitett_nev text check (megjelenitett_nev is null or char_length(trim(megjelenitett_nev)) between 2 and 40),
    profilkep_url text,
    is_admin boolean not null default false,
    created_at timestamptz not null default now()
);

alter table public.profiles
    add column if not exists is_admin boolean not null default false;

alter table public.profiles
    add column if not exists megjelenitett_nev text
        check (megjelenitett_nev is null or char_length(trim(megjelenitett_nev)) between 2 and 40);

alter table public.profiles
    add column if not exists profilkep_url text;

update public.profiles
set is_admin = true
where lower(felhasznalonev) = 'richard';

-- Migráció korábbi sémáról (nev/email -> felhasznalonev)
do $$
begin
    if exists (
        select 1 from information_schema.columns
        where table_schema = 'public' and table_name = 'profiles' and column_name = 'nev'
    ) and not exists (
        select 1 from information_schema.columns
        where table_schema = 'public' and table_name = 'profiles' and column_name = 'felhasznalonev'
    ) then
        alter table public.profiles rename column nev to felhasznalonev;
    end if;

    if exists (
        select 1 from information_schema.columns
        where table_schema = 'public' and table_name = 'profiles' and column_name = 'email'
    ) then
        alter table public.profiles drop column email;
    end if;
end $$;

create unique index if not exists idx_profiles_felhasznalonev_lower
    on public.profiles (lower(felhasznalonev));

-- Új regisztrációnál automatikusan elmenti a felhasználónevet a public.profiles táblába
create or replace function public.handle_new_user()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
declare
    v_felhasznalonev text;
begin
    v_felhasznalonev := coalesce(
        nullif(trim(new.raw_user_meta_data ->> 'felhasznalonev'), ''),
        split_part(coalesce(new.email, 'horgasz'), '@', 1)
    );

    insert into public.profiles (id, felhasznalonev)
    values (new.id, left(v_felhasznalonev, 30))
    on conflict (id) do update
        set felhasznalonev = excluded.felhasznalonev;

    return new;
end;
$$;

drop trigger if exists on_auth_user_created on auth.users;
create trigger on_auth_user_created
    after insert on auth.users
    for each row execute function public.handle_new_user();

-- Korábban regisztrált felhasználók pótlása a public.profiles táblába (ha kimaradt volna)
insert into public.profiles (id, felhasznalonev)
select
    u.id,
    left(
        coalesce(
            nullif(trim(u.raw_user_meta_data ->> 'felhasznalonev'), ''),
            split_part(coalesce(u.email, 'horgasz'), '@', 1)
        ),
        30
    )
from auth.users u
on conflict (id) do nothing;

-- 2. Túrák tábla (súlylimit nélkül)
create table if not exists public.turak (
    id uuid primary key default gen_random_uuid(),
    nev text not null check (char_length(trim(nev)) between 3 and 100),
    helyszin text not null check (char_length(trim(helyszin)) between 2 and 150),
    kezdo_datum date not null,
    napok_szama integer not null default 7 check (napok_szama between 1 and 30),
    created_by uuid references auth.users(id) on delete set null default auth.uid(),
    created_at timestamptz not null default now()
);

-- Ha korábbi sémában létezett a max_csomagsuly_kg oszlop, biztonságosan eltávolítjuk
alter table public.turak drop column if exists max_csomagsuly_kg;

-- 3. Résztvevők tábla (regisztrált felhasználóhoz kötve, adatmegőrző aktiv_e jelzővel)
create table if not exists public.resztvevok (
    id uuid primary key default gen_random_uuid(),
    tura_id uuid not null references public.turak(id) on delete cascade,
    user_id uuid references auth.users(id) on delete set null,
    nev text not null check (char_length(trim(nev)) between 2 and 80),
    szerepkor text not null default 'Horgász' check (char_length(trim(szerepkor)) between 1 and 60),
    aktiv_e boolean not null default true,
    created_at timestamptz not null default now()
);

alter table public.resztvevok
    add column if not exists user_id uuid references auth.users(id) on delete set null;

alter table public.resztvevok
    add column if not exists aktiv_e boolean not null default true;

-- Egy regisztrált felhasználó egy túrában csak egyszer szerepelhessen
create unique index if not exists idx_resztvevok_tura_user
    on public.resztvevok (tura_id, user_id)
    where user_id is not null;

-- 4. Felszerelés tételek tábla (közös vagy egyéni)
create table if not exists public.felszereles_tetelek (
    id uuid primary key default gen_random_uuid(),
    tura_id uuid not null references public.turak(id) on delete cascade,
    resztvevo_id uuid references public.resztvevok(id) on delete cascade,
    nev text not null check (char_length(trim(nev)) between 2 and 100),
    kategoria text not null default 'Egyéb' check (char_length(trim(kategoria)) between 1 and 50),
    mennyiseg integer not null default 1 check (mennyiseg between 1 and 500),
    bepakolva boolean not null default false,
    kozos_e boolean not null default false,
    created_at timestamptz not null default now(),
    constraint chk_kozos_resztvevo check (
        (kozos_e = true and resztvevo_id is null)
        or (kozos_e = false and resztvevo_id is not null)
    )
);

-- Ha korábbi sémában létezett az egyseg_suly_kg oszlop, biztonságosan eltávolítjuk
alter table public.felszereles_tetelek drop column if exists egyseg_suly_kg;

-- 5. Fogások tábla (súly+hossz alapú VAGY darabszám/mennyiség alapú, pl. kárász)
create table if not exists public.fogasok (
    id uuid primary key default gen_random_uuid(),
    tura_id uuid not null references public.turak(id) on delete cascade,
    resztvevo_id uuid not null references public.resztvevok(id) on delete cascade,
    halfaj text not null check (char_length(trim(halfaj)) between 2 and 60),
    darab_alapu_e boolean not null default false,
    mennyiseg integer not null default 1 check (mennyiseg between 1 and 500),
    visszaengedett_e boolean not null default true,
    suly_kg double precision check (suly_kg is null or suly_kg between 0.05 and 250.0),
    hossz_cm double precision check (hossz_cm is null or hossz_cm between 5.0 and 350.0),
    idopont timestamptz not null default now(),
    csali text not null check (char_length(trim(csali)) between 2 and 80),
    megjegyzes text check (megjegyzes is null or char_length(megjegyzes) <= 500),
    kep_url text,
    created_by uuid references auth.users(id) on delete set null default auth.uid(),
    created_at timestamptz not null default now()
);

-- Migráció meglévő fogasok táblára (darabszám alapú fogások és megtartott/visszaengedett támogatása)
alter table public.fogasok add column if not exists darab_alapu_e boolean not null default false;
alter table public.fogasok add column if not exists mennyiseg integer not null default 1;
alter table public.fogasok add column if not exists visszaengedett_e boolean not null default true;
alter table public.fogasok alter column suly_kg drop not null;
alter table public.fogasok alter column hossz_cm drop not null;

alter table public.fogasok drop constraint if exists fogasok_suly_kg_check;
alter table public.fogasok add constraint fogasok_suly_kg_check
    check (suly_kg is null or suly_kg between 0.05 and 250.0);

alter table public.fogasok drop constraint if exists fogasok_hossz_cm_check;
alter table public.fogasok add constraint fogasok_hossz_cm_check
    check (hossz_cm is null or hossz_cm between 5.0 and 350.0);

alter table public.fogasok drop constraint if exists fogasok_mennyiseg_check;
alter table public.fogasok add constraint fogasok_mennyiseg_check
    check (mennyiseg between 1 and 500);

alter table public.fogasok drop constraint if exists chk_fogas_meresi_mod;
alter table public.fogasok add constraint chk_fogas_meresi_mod check (
    (darab_alapu_e = false and suly_kg is not null and hossz_cm is not null and mennyiseg = 1)
    or (darab_alapu_e = true and suly_kg is null and hossz_cm is null and mennyiseg >= 1)
);

-- Indexek a gyors lekérdezésekhez
create index if not exists idx_resztvevok_tura_id on public.resztvevok(tura_id);
create index if not exists idx_felszereles_tura_id on public.felszereles_tetelek(tura_id);
create index if not exists idx_felszereles_resztvevo_id on public.felszereles_tetelek(resztvevo_id);
create index if not exists idx_fogasok_tura_id on public.fogasok(tura_id);
create index if not exists idx_fogasok_resztvevo_id on public.fogasok(resztvevo_id);
create index if not exists idx_fogasok_idopont on public.fogasok(idopont desc);

-- =============================================================================
-- Row Level Security (RLS) - Csak bejelentkezett felhasználók férnek hozzá!
-- =============================================================================

alter table public.profiles enable row level security;
alter table public.turak enable row level security;
alter table public.resztvevok enable row level security;
alter table public.felszereles_tetelek enable row level security;
alter table public.fogasok enable row level security;

drop policy if exists "profiles_select_authenticated" on public.profiles;
create policy "profiles_select_authenticated"
    on public.profiles for select
    to authenticated
    using (true);

drop policy if exists "profiles_insert_own" on public.profiles;
create policy "profiles_insert_own"
    on public.profiles for insert
    to authenticated
    with check (auth.uid() = id);

drop policy if exists "profiles_update_own" on public.profiles;
create policy "profiles_update_own"
    on public.profiles for update
    to authenticated
    using (auth.uid() = id)
    with check (auth.uid() = id);

drop policy if exists "turak_all_authenticated" on public.turak;
create policy "turak_all_authenticated"
    on public.turak for all
    to authenticated
    using (true)
    with check (true);

drop policy if exists "resztvevok_all_authenticated" on public.resztvevok;
create policy "resztvevok_all_authenticated"
    on public.resztvevok for all
    to authenticated
    using (true)
    with check (true);

drop policy if exists "felszereles_all_authenticated" on public.felszereles_tetelek;
create policy "felszereles_all_authenticated"
    on public.felszereles_tetelek for all
    to authenticated
    using (true)
    with check (true);

drop policy if exists "fogasok_all_authenticated" on public.fogasok;
create policy "fogasok_all_authenticated"
    on public.fogasok for all
    to authenticated
    using (true)
    with check (true);

-- =============================================================================
-- Supabase Storage bucket a fogási napló képeihez
-- =============================================================================

insert into storage.buckets (id, name, public)
values ('fogas-kepek', 'fogas-kepek', true)
on conflict (id) do update set public = true;

drop policy if exists "fogas_kepek_public_read" on storage.objects;
create policy "fogas_kepek_public_read"
    on storage.objects for select
    to public
    using (bucket_id = 'fogas-kepek');

drop policy if exists "fogas_kepek_auth_insert" on storage.objects;
create policy "fogas_kepek_auth_insert"
    on storage.objects for insert
    to authenticated
    with check (bucket_id = 'fogas-kepek');

drop policy if exists "fogas_kepek_auth_update" on storage.objects;
create policy "fogas_kepek_auth_update"
    on storage.objects for update
    to authenticated
    using (bucket_id = 'fogas-kepek')
    with check (bucket_id = 'fogas-kepek');

drop policy if exists "fogas_kepek_auth_delete" on storage.objects;
create policy "fogas_kepek_auth_delete"
    on storage.objects for delete
    to authenticated
    using (bucket_id = 'fogas-kepek');

-- =============================================================================
-- 6. Hal főkategóriák és alkategóriák (adatmegőrző katalógus táblák)
-- =============================================================================

create table if not exists public.hal_kategoriak (
    id uuid primary key default gen_random_uuid(),
    nev text not null check (char_length(trim(nev)) between 2 and 60),
    sorrend integer not null default 100,
    created_at timestamptz not null default now()
);

create unique index if not exists idx_hal_kategoriak_nev_lower
    on public.hal_kategoriak (lower(nev));

create table if not exists public.hal_alkategoriak (
    id uuid primary key default gen_random_uuid(),
    kategoria_id uuid not null references public.hal_kategoriak(id) on delete cascade,
    nev text not null check (char_length(trim(nev)) between 2 and 60),
    sorrend integer not null default 100,
    created_at timestamptz not null default now()
);

create unique index if not exists idx_hal_alkategoriak_kat_nev_lower
    on public.hal_alkategoriak (kategoria_id, lower(nev));

create index if not exists idx_hal_alkategoriak_kategoria_id
    on public.hal_alkategoriak (kategoria_id);

alter table public.hal_kategoriak enable row level security;
alter table public.hal_alkategoriak enable row level security;

drop policy if exists "hal_kategoriak_all_authenticated" on public.hal_kategoriak;
create policy "hal_kategoriak_all_authenticated"
    on public.hal_kategoriak for all
    to authenticated
    using (true)
    with check (true);

drop policy if exists "hal_alkategoriak_all_authenticated" on public.hal_alkategoriak;
create policy "hal_alkategoriak_all_authenticated"
    on public.hal_alkategoriak for all
    to authenticated
    using (true)
    with check (true);

insert into public.hal_kategoriak (nev, sorrend)
values
    ('Ponty', 10),
    ('Amur', 20),
    ('Kárász', 30),
    ('Keszegfélék', 40),
    ('Harcsa', 50),
    ('Ragadozók', 60),
    ('Busa', 70),
    ('Egyéb békés halak', 80)
on conflict (lower(nev)) do nothing;

with seed_alkategoriak (kategoria_nev, alkategoria_nev, sorrend) as (
    values
        ('Ponty', 'Tükörponty', 10),
        ('Ponty', 'Pikkelyes ponty (Tőponty)', 20),
        ('Ponty', 'Nyurgaponty', 30),
        ('Ponty', 'Bőrponty', 40),
        ('Ponty', 'Koi ponty', 50),
        ('Amur', 'Fehér amur', 10),
        ('Amur', 'Fekete amur', 20),
        ('Kárász', 'Ezüstkárász', 10),
        ('Kárász', 'Aranykárász', 20),
        ('Keszegfélék', 'Dévérkeszeg', 10),
        ('Keszegfélék', 'Karikakeszeg', 20),
        ('Keszegfélék', 'Vörösszárnyú keszeg', 30),
        ('Keszegfélék', 'Bodorka', 40),
        ('Keszegfélék', 'Bagolykeszeg', 50),
        ('Keszegfélék', 'Lapos keszeg', 60),
        ('Harcsa', 'Lesőharcsa (Európai harcsa)', 10),
        ('Harcsa', 'Törpeharcsa', 20),
        ('Harcsa', 'Afrikai harcsa', 30),
        ('Ragadozók', 'Csuka', 10),
        ('Ragadozók', 'Fogassüllő (Süllő)', 20),
        ('Ragadozók', 'Kősüllő', 30),
        ('Ragadozók', 'Balin', 40),
        ('Ragadozók', 'Sügér', 50),
        ('Ragadozók', 'Fekete sügér', 60),
        ('Ragadozók', 'Domolykó', 70),
        ('Busa', 'Fehér busa', 10),
        ('Busa', 'Pettyes busa', 20),
        ('Egyéb békés halak', 'Compó', 10),
        ('Egyéb békés halak', 'Márna', 20),
        ('Egyéb békés halak', 'Paduc', 30),
        ('Egyéb békés halak', 'Jászkeszeg', 40),
        ('Egyéb békés halak', 'Tokhal (Kecsege / Szibériai tok)', 50),
        ('Egyéb békés halak', 'Pisztráng', 60),
        ('Egyéb békés halak', 'Angolna', 70)
)
insert into public.hal_alkategoriak (kategoria_id, nev, sorrend)
select k.id, s.alkategoria_nev, s.sorrend
from seed_alkategoriak s
join public.hal_kategoriak k on lower(k.nev) = lower(s.kategoria_nev)
on conflict (kategoria_id, lower(nev)) do nothing;

-- =============================================================================
-- 7. Túra költségek és személyenkénti leosztás (adatmegőrző)
-- =============================================================================

create table if not exists public.koltseg_tetelek (
    id uuid primary key default gen_random_uuid(),
    tura_id uuid not null references public.turak(id) on delete cascade,
    nev text not null check (char_length(trim(nev)) between 2 and 100),
    osszeg numeric(12, 2) not null default 0 check (osszeg >= 0 and osszeg <= 100000000),
    kozos_e boolean not null default true,
    resztvevo_id uuid references public.resztvevok(id) on delete cascade,
    fizeto_resztvevo_id uuid references public.resztvevok(id) on delete set null,
    megjegyzes text check (megjegyzes is null or char_length(megjegyzes) <= 300),
    created_by uuid references auth.users(id) on delete set null default auth.uid(),
    created_at timestamptz not null default now(),
    constraint chk_koltseg_kozos_resztvevo check (
        (kozos_e = true and resztvevo_id is null)
        or (kozos_e = false and resztvevo_id is not null)
    )
);

create index if not exists idx_koltseg_tetelek_tura_id on public.koltseg_tetelek(tura_id);
create index if not exists idx_koltseg_tetelek_resztvevo_id on public.koltseg_tetelek(resztvevo_id);
create index if not exists idx_koltseg_tetelek_fizeto_id on public.koltseg_tetelek(fizeto_resztvevo_id);

alter table public.koltseg_tetelek enable row level security;

drop policy if exists "koltseg_tetelek_all_authenticated" on public.koltseg_tetelek;
create policy "koltseg_tetelek_all_authenticated"
    on public.koltseg_tetelek for all
    to authenticated
    using (true)
    with check (true);

-- =============================================================================
-- 8. Újrahasználható csomaglisták (sablonok) és tételeik (adatmegőrző)
-- =============================================================================

create table if not exists public.csomaglista_sablonok (
    id uuid primary key default gen_random_uuid(),
    nev text not null check (char_length(trim(nev)) between 2 and 100),
    leiras text check (leiras is null or char_length(leiras) <= 300),
    created_by uuid references auth.users(id) on delete set null default auth.uid(),
    created_at timestamptz not null default now()
);

create table if not exists public.csomaglista_sablon_tetelek (
    id uuid primary key default gen_random_uuid(),
    sablon_id uuid not null references public.csomaglista_sablonok(id) on delete cascade,
    nev text not null check (char_length(trim(nev)) between 2 and 100),
    kategoria text not null default 'Egyéb' check (char_length(trim(kategoria)) between 1 and 50),
    mennyiseg integer not null default 1 check (mennyiseg between 1 and 500),
    kozos_e boolean not null default true,
    created_at timestamptz not null default now()
);

create index if not exists idx_sablon_tetelek_sablon_id
    on public.csomaglista_sablon_tetelek(sablon_id);

alter table public.csomaglista_sablonok enable row level security;
alter table public.csomaglista_sablon_tetelek enable row level security;

drop policy if exists "csomaglista_sablonok_all_authenticated" on public.csomaglista_sablonok;
create policy "csomaglista_sablonok_all_authenticated"
    on public.csomaglista_sablonok for all
    to authenticated
    using (true)
    with check (true);

drop policy if exists "csomaglista_sablon_tetelek_all_authenticated" on public.csomaglista_sablon_tetelek;
create policy "csomaglista_sablon_tetelek_all_authenticated"
    on public.csomaglista_sablon_tetelek for all
    to authenticated
    using (true)
    with check (true);
