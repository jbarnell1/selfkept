-- Manual, reviewed rollback of 0020_lab_reseed. Never run automatically.
-- Removes the reference rows and aliases of the markers added with the cross-reference package.
-- Rows that 0007 seeded are left as 0020 last wrote them.
-- After applying by hand, delete the journal row:
--   DELETE FROM schemaversions WHERE scriptname LIKE '%0020_lab_reseed.up.sql';

DELETE FROM lab_marker_alias WHERE metric_key IN (
  'lab.alt', 'lab.ast', 'lab.platelets', 'lab.wbc', 'lab.rbc', 'lab.mcv', 'lab.hba1c',
  'lab.glucose_fasting', 'lab.cystatin_c', 'lab.transferrin_saturation', 'lab.iron', 'lab.tibc'
);
DELETE FROM lab_reference WHERE metric_key IN (
  'lab.alt', 'lab.ast', 'lab.platelets', 'lab.wbc', 'lab.rbc', 'lab.mcv', 'lab.hba1c',
  'lab.glucose_fasting', 'lab.cystatin_c', 'lab.transferrin_saturation', 'lab.iron', 'lab.tibc'
);
