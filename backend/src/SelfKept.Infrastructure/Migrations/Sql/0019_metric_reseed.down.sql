-- Manual, reviewed rollback of 0019_metric_reseed. Never run automatically.
-- Removes the lab markers added with the cross-reference package. Apply the down of
-- 0020_lab_reseed first: its reference rows and aliases point at these keys. The foreign keys
-- from sample and daily_summary refuse while any data references one of them, which is the
-- right guard. Display text changed by the reseed is not put back.
-- After applying by hand, delete the journal row:
--   DELETE FROM schemaversions WHERE scriptname LIKE '%0019_metric_reseed.up.sql';

DELETE FROM metric WHERE key IN (
  'lab.alt', 'lab.ast', 'lab.platelets', 'lab.wbc', 'lab.rbc', 'lab.mcv', 'lab.hba1c',
  'lab.glucose_fasting', 'lab.cystatin_c', 'lab.transferrin_saturation', 'lab.iron', 'lab.tibc'
);
