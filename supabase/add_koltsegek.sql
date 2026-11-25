-- =============================================================================
-- FishingPlanner - Költségek és személyenkénti leosztás tábla (adatmegőrző)
-- Futtasd le a Supabase Dashboard -> SQL Editor felületén!
-- Az SQL szkript 100%-ban idempotens és nem bántja a meglévő adatokat.
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
