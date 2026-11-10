-- Reverse of 0017_labs_review. Manual, reviewed rollback only; never auto-run.
-- Remove the matching row from schemaversions after applying:
--   DELETE FROM schemaversions WHERE scriptname LIKE '%0017_labs_review.up.sql';
-- Apply the downs of 0020 and 0019 first if they ran: 0020 writes the age columns.
DROP TABLE IF EXISTS trial;
ALTER TABLE lab_reference DROP COLUMN IF EXISTS older_high;
ALTER TABLE lab_reference DROP COLUMN IF EXISTS older_low;
ALTER TABLE lab_reference DROP COLUMN IF EXISTS age_split_at;
ALTER TABLE lab_report DROP CONSTRAINT IF EXISTS ck_lab_report_draw_period;
ALTER TABLE lab_report DROP CONSTRAINT IF EXISTS ck_lab_report_fasting_hours;
ALTER TABLE lab_report DROP CONSTRAINT IF EXISTS ck_lab_report_fasting;
ALTER TABLE lab_report DROP COLUMN IF EXISTS conditions_defaulted;
ALTER TABLE lab_report DROP COLUMN IF EXISTS unwell;
ALTER TABLE lab_report DROP COLUMN IF EXISTS hard_training;
ALTER TABLE lab_report DROP COLUMN IF EXISTS draw_period;
ALTER TABLE lab_report DROP COLUMN IF EXISTS draw_time;
ALTER TABLE lab_report DROP COLUMN IF EXISTS fasting_hours;
ALTER TABLE lab_report DROP COLUMN IF EXISTS fasting;
