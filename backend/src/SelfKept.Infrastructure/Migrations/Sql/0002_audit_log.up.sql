-- 0002_audit_log: append-only record of security-relevant events (sign-up, sign-in, invites, sharing).
-- Not a foreign key to user_account: an audit row must outlive the account it describes.

CREATE TABLE IF NOT EXISTS audit_log (
  id            uuid        NOT NULL,
  occurred_at   timestamptz NOT NULL DEFAULT now(),
  action        varchar(64) NOT NULL,
  actor_user_id uuid        NULL,
  household_id  uuid        NULL,
  target_type   varchar(64) NULL,
  target_id     varchar(64) NULL,
  details       jsonb       NULL,
  ip_address    varchar(64) NULL,
  CONSTRAINT pk_audit_log PRIMARY KEY (id)
);
CREATE INDEX IF NOT EXISTS ix_audit_log_occurred_at ON audit_log (occurred_at);
CREATE INDEX IF NOT EXISTS ix_audit_log_actor_user_id ON audit_log (actor_user_id);
