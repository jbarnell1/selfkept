-- Reverse of 0022_photo. Applied by hand; see README.md. Photo entries must go before the source
-- check can be narrowed again; their samples under the "Photo" source go with that source.
DELETE FROM sample WHERE source_id IN (SELECT id FROM source WHERE kind = 'manual' AND device_name = 'Photo' AND platform = 'server');
DELETE FROM source WHERE kind = 'manual' AND device_name = 'Photo' AND platform = 'server';
DELETE FROM journal_entry WHERE source = 'photo';
ALTER TABLE journal_entry DROP CONSTRAINT IF EXISTS ck_journal_entry_source;
ALTER TABLE journal_entry ADD CONSTRAINT ck_journal_entry_source CHECK (source IN ('chip', 'typed', 'assistant', 'checkin'));
ALTER TABLE journal_entry DROP CONSTRAINT IF EXISTS ck_journal_entry_nutrition_basis;
ALTER TABLE journal_entry DROP COLUMN IF EXISTS fat_g;
ALTER TABLE journal_entry DROP COLUMN IF EXISTS carbs_g;
ALTER TABLE journal_entry DROP COLUMN IF EXISTS protein_g;
ALTER TABLE journal_entry DROP COLUMN IF EXISTS energy_kcal;
ALTER TABLE journal_entry DROP COLUMN IF EXISTS nutrition_basis;
UPDATE metric SET blurb = 'Calories eaten, as logged in another app.' WHERE key = 'energy_intake_kcal';
UPDATE metric SET blurb = 'Protein eaten, as logged in another app.' WHERE key = 'protein_g';
UPDATE metric SET blurb = 'Carbohydrate eaten, as logged in another app.' WHERE key = 'carbs_g';
UPDATE metric SET blurb = 'Fat eaten, as logged in another app.' WHERE key = 'fat_g';
