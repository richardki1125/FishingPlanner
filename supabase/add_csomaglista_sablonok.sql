-- =============================================================================
-- FishingPlanner - Újrahasználható csomaglisták (sablonok) táblái (adatmegőrző)
-- Futtasd le a Supabase Dashboard -> SQL Editor felületén!
-- Az SQL szkript 100%-ban idempotens és nem bántja a meglévő adatokat.
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
