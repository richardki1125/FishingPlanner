-- =============================================================================
-- FishingPlanner - Hal főkategóriák és alkategóriák (100% adatmegőrző kiegészítés)
-- CSAK új táblákat hoz létre és alapadatokat szúr be (ON CONFLICT DO NOTHING).
-- A meglévő túrákat, résztvevőket, csomaglistákat és fogásokat NEM módosítja és NEM törli!
-- Futtasd le a Supabase Dashboard -> SQL Editor felületén!
-- =============================================================================

create extension if not exists "pgcrypto";

-- 1. Hal főkategóriák tábla (pl. Ponty, Amur, Kárász, Ragadozók)
create table if not exists public.hal_kategoriak (
    id uuid primary key default gen_random_uuid(),
    nev text not null check (char_length(trim(nev)) between 2 and 60),
    sorrend integer not null default 100,
    created_at timestamptz not null default now()
);

create unique index if not exists idx_hal_kategoriak_nev_lower
    on public.hal_kategoriak (lower(nev));

-- 2. Hal alkategóriák tábla (pl. Ponty -> Tükörponty, Pikkelyes ponty, Bőrponty)
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

-- 3. Row Level Security (RLS) - minden bejelentkezett horgász olvashatja és bővítheti
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

-- 4. Alapértelmezett magyar horgász halfaj-katalógus feltöltése (meglévő sorokat nem ír felül)
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
        -- Ponty
        ('Ponty', 'Tükörponty', 10),
        ('Ponty', 'Pikkelyes ponty (Tőponty)', 20),
        ('Ponty', 'Nyurgaponty', 30),
        ('Ponty', 'Bőrponty', 40),
        ('Ponty', 'Koi ponty', 50),
        -- Amur
        ('Amur', 'Fehér amur', 10),
        ('Amur', 'Fekete amur', 20),
        -- Kárász
        ('Kárász', 'Ezüstkárász', 10),
        ('Kárász', 'Aranykárász', 20),
        -- Keszegfélék
        ('Keszegfélék', 'Dévérkeszeg', 10),
        ('Keszegfélék', 'Karikakeszeg', 20),
        ('Keszegfélék', 'Vörösszárnyú keszeg', 30),
        ('Keszegfélék', 'Bodorka', 40),
        ('Keszegfélék', 'Bagolykeszeg', 50),
        ('Keszegfélék', 'Lapos keszeg', 60),
        -- Harcsa
        ('Harcsa', 'Lesőharcsa (Európai harcsa)', 10),
        ('Harcsa', 'Törpeharcsa', 20),
        ('Harcsa', 'Afrikai harcsa', 30),
        -- Ragadozók
        ('Ragadozók', 'Csuka', 10),
        ('Ragadozók', 'Fogassüllő (Süllő)', 20),
        ('Ragadozók', 'Kősüllő', 30),
        ('Ragadozók', 'Balin', 40),
        ('Ragadozók', 'Sügér', 50),
        ('Ragadozók', 'Fekete sügér', 60),
        ('Ragadozók', 'Domolykó', 70),
        -- Busa
        ('Busa', 'Fehér busa', 10),
        ('Busa', 'Pettyes busa', 20),
        -- Egyéb békés halak
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
