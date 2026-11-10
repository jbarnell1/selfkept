-- Manual, reviewed rollback of 0014_metric_reseed. Never run automatically.
-- Removes the metrics added since 0004. The foreign keys from sample and daily_summary refuse
-- while any data references one of them, which is the right guard: delete that data first if
-- that is the intent. Display text changed by the reseed is not put back.
-- After applying by hand, delete the journal row:
--   DELETE FROM schemaversions WHERE scriptname LIKE '%0014_metric_reseed.up.sql';

DELETE FROM metric WHERE key IN (
  'waist_cm', 'lean_mass_kg', 'bone_mass_kg', 'body_water_kg',
  'energy_intake_kcal', 'protein_g', 'carbs_g', 'fat_g'
);
