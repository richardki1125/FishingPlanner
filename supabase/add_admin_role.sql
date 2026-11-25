-- =============================================================================
-- FishingPlanner - Admin jogosultság (csak a tulajdonos látja a Beállítások fület)
-- 100% adatmegőrző kiegészítés: a meglévő adatokat NEM törli és NEM írja felül!
-- Futtasd le a Supabase Dashboard -> SQL Editor felületén!
-- =============================================================================

alter table public.profiles
    add column if not exists is_admin boolean not null default false;

-- A tulajdonos fiók (Richard) beállítása adminnak
update public.profiles
set is_admin = true
where lower(felhasznalonev) = 'richard';
