-- 0022_photo: photos read by the home model (docs/photo-design.md). There is no photo table: the
-- photo is never stored. A confirmed food or drink entry may carry numbers, with where they came
-- from (estimate from a meal photo, or label). The service refuses a basis without a number and a
-- number without a basis. Mirrored by the JournalEntry mapping in SelfKeptDbContext.

ALTER TABLE journal_entry ADD COLUMN IF NOT EXISTS nutrition_basis varchar(16) NULL;
ALTER TABLE journal_entry ADD COLUMN IF NOT EXISTS energy_kcal numeric(7,1) NULL;
ALTER TABLE journal_entry ADD COLUMN IF NOT EXISTS protein_g numeric(7,1) NULL;
ALTER TABLE journal_entry ADD COLUMN IF NOT EXISTS carbs_g numeric(7,1) NULL;
ALTER TABLE journal_entry ADD COLUMN IF NOT EXISTS fat_g numeric(7,1) NULL;

ALTER TABLE journal_entry DROP CONSTRAINT IF EXISTS ck_journal_entry_nutrition_basis;
ALTER TABLE journal_entry ADD CONSTRAINT ck_journal_entry_nutrition_basis CHECK (nutrition_basis IS NULL OR nutrition_basis IN ('estimate', 'label'));

-- A confirmed photo entry has its own source. Widening a check is additive: every row that passed
-- the old check passes this one.
ALTER TABLE journal_entry DROP CONSTRAINT IF EXISTS ck_journal_entry_source;
ALTER TABLE journal_entry ADD CONSTRAINT ck_journal_entry_source CHECK (source IN ('chip', 'typed', 'assistant', 'checkin', 'photo'));

-- The four intake metrics can now come from a confirmed photo as well as from another app. Fresh
-- installs get the same text from 0019_metric_reseed, which is generated from MetricSeed.cs.
UPDATE metric SET blurb = 'Calories eaten, from a photo you confirmed or as logged in another app.' WHERE key = 'energy_intake_kcal';
UPDATE metric SET blurb = 'Protein eaten, from a photo you confirmed or as logged in another app.' WHERE key = 'protein_g';
UPDATE metric SET blurb = 'Carbohydrate eaten, from a photo you confirmed or as logged in another app.' WHERE key = 'carbs_g';
UPDATE metric SET blurb = 'Fat eaten, from a photo you confirmed or as logged in another app.' WHERE key = 'fat_g';
