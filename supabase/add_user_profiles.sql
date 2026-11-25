-- =============================================================================
-- FishingPlanner - Felhasználói profil (megjelenített név + profilkép)
-- 100% adatmegőrző kiegészítés: a meglévő adatokat NEM törli és NEM írja felül!
-- Futtasd le a Supabase Dashboard -> SQL Editor felületén!
-- =============================================================================

alter table public.profiles
    add column if not exists megjelenitett_nev text
        check (megjelenitett_nev is null or char_length(trim(megjelenitett_nev)) between 2 and 40);

alter table public.profiles
    add column if not exists profilkep_url text;
