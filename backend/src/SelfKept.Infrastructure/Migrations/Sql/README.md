# SQL migrations

Hand-written, SQL-first migrations applied by [`SqlMigrationRunner`](../../Data/SqlMigrationRunner.cs)
via DbUp on app startup. There is no EF model snapshot and no `dotnet ef migrations`; the EF model
in `SelfKeptDbContext` must be kept in step with these files by hand.

## Convention

Each migration is a pair, numbered in order:

```
NNNN_short_description.up.sql     applied forward by DbUp (embedded, journaled to schemaversions)
NNNN_short_description.down.sql   committed for manual, reviewed rollback (never auto-run)
```

- Only `*.up.sql` files are embedded (see the `.csproj` `EmbeddedResource` glob) and run on
  startup, in filename order. Keep the numeric prefix zero-padded and monotonic. The next free
  number is 0011.
- `*.down.sql` is the reviewed reverse of the up script. Downs are not auto-run: rolling back a
  schema is a deliberate, supervised act. Apply a down by hand (psql) and delete the matching
  `schemaversions` row.
- Write plain, idempotent-friendly SQL (`IF NOT EXISTS`).
- Names are snake_case: tables singular (`person`, `share_grant`), constraints `pk_`, `fk_`, `ck_`,
  indexes `ix_<table>_<columns>`.

## Transactions

The runner wraps every script in `BEGIN; ... COMMIT;` together with its journal row, so a script
applies fully or not at all. **Do not write your own BEGIN/COMMIT.** Scripts that cannot run inside
a transaction (`CREATE INDEX CONCURRENTLY`, `VACUUM`, `CREATE DATABASE`) opt out with a line:

```sql
-- selfkept:no-transaction
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_example ON example (col);
```

An opted-out script can stop half way, so write it to be safe to re-run.

## Rules the lint enforces (`MigrationLintTests`)

- Names look like `NNNN_snake_case.up.sql` and numbers are unique.
- Every `.up.sql` has a matching `.down.sql`.
- No `DROP TABLE/COLUMN/SCHEMA`, `TRUNCATE`, `DELETE` without `WHERE`, or `ALTER COLUMN ... TYPE`
  unless the script has a line `-- selfkept:reviewed-destructive: <reason>`. Forward migrations
  are additive; drop or retype only in a later release, once no running version uses the old shape.
- Statements that cannot run in a transaction need the `-- selfkept:no-transaction` marker.
- Scripts must not open their own `BEGIN` or `COMMIT`.

## Backup before migrating

`Migrations:BackupBeforeMigrate=true` makes the runner take a `pg_dump` (custom format) into
`Migrations:BackupDirectory` before applying pending scripts to an existing database, then check it
with `pg_restore --list`. If the dump or the check fails the server refuses to migrate, unless
`Migrations:AllowMigrateWithoutBackup=true`. `PgDumpPath`, `PgRestorePath` and `KeepBackups`
(default 5) are also settings. The `pg_dump` client must be the same major version as the server
or newer. The default is off; the self-host image turns it on.

## Database image

The schema targets PostgreSQL 16. `pgvector/pgvector:pg16` is the recommended image so a later
package can add vector columns without a database swap; nothing in these scripts needs the
extension yet.
