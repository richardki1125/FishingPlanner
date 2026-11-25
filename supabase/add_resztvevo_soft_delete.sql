-- =============================================================================
-- FishingPlanner - Résztvevők adatmegőrző eltávolítása (Soft-Delete: aktiv_e)
-- 100% adatmegőrző kiegészítés: ha eltávolítasz valakit egy túráról, a feltöltött
-- fogásai és felszerelései MEGMARADNAK, visszavételkor pedig automatikusan visszakapcsolódnak!
-- Futtasd le a Supabase Dashboard -> SQL Editor felületén!
-- =============================================================================

alter table public.resztvevok
    add column if not exists aktiv_e boolean not null default true;
