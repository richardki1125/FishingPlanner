-- =============================================================================
-- FishingPlanner - Megtartott / Visszaengedett mező a fogási naplóhoz
-- 100% adatmegőrző kiegészítés: a meglévő fogásokat NEM törli és NEM írja felül,
-- csak hozzáadja az új visszaengedett_e oszlopot (alapértelmezetten: true = visszaengedett).
-- Futtasd le a Supabase Dashboard -> SQL Editor felületén!
-- =============================================================================

alter table public.fogasok
    add column if not exists visszaengedett_e boolean not null default true;
