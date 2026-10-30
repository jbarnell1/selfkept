using System.Text.RegularExpressions;
using System.Text;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;

namespace SelfKept.Infrastructure.Data;

/// <summary>
/// EF Core is used for queries and writes only. The schema is owned by the SQL-first migrations
/// in Migrations/Sql (DbUp); this model must match them. Table and column names are snake_case.
/// </summary>
public class SelfKeptDbContext : DbContext
{
  public SelfKeptDbContext(DbContextOptions<SelfKeptDbContext> options) : base(options) { }

  public DbSet<User> Users => Set<User>();
  public DbSet<Person> Persons => Set<Person>();
  public DbSet<Household> Households => Set<Household>();
  public DbSet<HouseholdMember> HouseholdMembers => Set<HouseholdMember>();
  public DbSet<HouseholdInvite> HouseholdInvites => Set<HouseholdInvite>();
  public DbSet<ShareGrant> ShareGrants => Set<ShareGrant>();
  public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
  public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
  public DbSet<Metric> Metrics => Set<Metric>();
  public DbSet<Source> Sources => Set<Source>();
  public DbSet<Sample> Samples => Set<Sample>();
  public DbSet<Workout> Workouts => Set<Workout>();
  public DbSet<Checkin> Checkins => Set<Checkin>();
  public DbSet<DailySummary> DailySummaries => Set<DailySummary>();
  public DbSet<Baseline> Baselines => Set<Baseline>();
  public DbSet<Insight> Insights => Set<Insight>();
  public DbSet<AiCall> AiCalls => Set<AiCall>();
  public DbSet<TodayCache> TodayCaches => Set<TodayCache>();
  public DbSet<LabReport> LabReports => Set<LabReport>();
  public DbSet<LabResult> LabResults => Set<LabResult>();
  public DbSet<LabMarkerAlias> LabMarkerAliases => Set<LabMarkerAlias>();
  public DbSet<LabReference> LabReferences => Set<LabReference>();
  public DbSet<OAuthClient> OAuthClients => Set<OAuthClient>();
  public DbSet<OAuthAuthorizationRequest> OAuthAuthorizationRequests => Set<OAuthAuthorizationRequest>();
  public DbSet<OAuthRefreshToken> OAuthRefreshTokens => Set<OAuthRefreshToken>();
  public DbSet<PersonalAccessToken> PersonalAccessTokens => Set<PersonalAccessToken>();
  public DbSet<AiMemory> AiMemories => Set<AiMemory>();
  public DbSet<OuraConnection> OuraConnections => Set<OuraConnection>();
  public DbSet<OuraAuthState> OuraAuthStates => Set<OuraAuthState>();
  public DbSet<UserIdentity> UserIdentities => Set<UserIdentity>();
  public DbSet<SsoAuthState> SsoAuthStates => Set<SsoAuthState>();
  public DbSet<SsoHandoff> SsoHandoffs => Set<SsoHandoff>();
  public DbSet<ServerSetting> ServerSettings => Set<ServerSetting>();

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    ConfigureCollect(modelBuilder);
    ConfigureInterpret(modelBuilder);
    ConfigureLabs(modelBuilder);
    ConfigureConnect(modelBuilder);
    ConfigureSso(modelBuilder);

    modelBuilder.Entity<User>(e =>
    {
      e.ToTable("user_account");
      e.HasKey(x => x.Id);
      e.Property(x => x.Email).HasMaxLength(254).IsRequired();
      e.HasIndex(x => x.Email).IsUnique();
      e.Property(x => x.PasswordHash).IsRequired();
      e.Property(x => x.DisplayName).HasMaxLength(80).IsRequired();
      e.HasOne(x => x.Person).WithOne().HasForeignKey<User>(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
      e.HasIndex(x => x.PersonId).IsUnique();
      e.HasOne(x => x.Household).WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.SetNull);
      e.Property(x => x.SetupPlan).HasColumnType("varchar(32)[]");
    });

    modelBuilder.Entity<ServerSetting>(e =>
    {
      e.ToTable("server_setting");
      e.HasKey(x => x.Key);
      e.Property(x => x.Key).HasMaxLength(100);
      e.Property(x => x.Value).IsRequired();
      e.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedBy).OnDelete(DeleteBehavior.SetNull);
    });

    modelBuilder.Entity<Person>(e =>
    {
      e.ToTable("person");
      e.HasKey(x => x.Id);
      e.Property(x => x.DisplayName).HasMaxLength(80).IsRequired();
      e.Property(x => x.SexAtBirth).HasConversion(LowercaseEnum<SexAtBirth>()).HasMaxLength(16);
      e.Property(x => x.Units).HasConversion(LowercaseEnum<UnitSystem>()).HasMaxLength(16);
      e.Property(x => x.TimeZone).HasMaxLength(64).IsRequired();
    });

    modelBuilder.Entity<Household>(e =>
    {
      e.ToTable("household");
      e.HasKey(x => x.Id);
      e.Property(x => x.Name).HasMaxLength(80).IsRequired();
      e.HasMany(x => x.Members).WithOne(m => m.Household).HasForeignKey(m => m.HouseholdId).OnDelete(DeleteBehavior.Cascade);
    });

    modelBuilder.Entity<HouseholdMember>(e =>
    {
      e.ToTable("household_member");
      e.HasKey(x => x.Id);
      e.Property(x => x.Role).HasConversion(LowercaseEnum<HouseholdRole>()).HasMaxLength(16);
      e.HasIndex(x => new { x.HouseholdId, x.PersonId }).IsUnique();
      e.HasOne(x => x.Person).WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Cascade);
      e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
    });

    modelBuilder.Entity<HouseholdInvite>(e =>
    {
      e.ToTable("household_invite");
      e.HasKey(x => x.Id);
      e.Property(x => x.CodeHash).HasMaxLength(64).IsRequired();
      e.HasIndex(x => x.CodeHash).IsUnique();
      // The update that spends a code only lands while used_at is still null.
      e.Property(x => x.UsedAt).IsConcurrencyToken();
      e.HasOne(x => x.Household).WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Cascade);
    });

    modelBuilder.Entity<ShareGrant>(e =>
    {
      e.ToTable("share_grant");
      e.HasKey(x => x.Id);
      e.Property(x => x.Category).HasConversion(LowercaseEnum<ShareCategory>()).HasMaxLength(16);
      e.HasIndex(x => new { x.OwnerPersonId, x.ViewerPersonId, x.Category }).IsUnique();
      e.HasIndex(x => x.ViewerPersonId);
    });

    modelBuilder.Entity<RefreshToken>(e =>
    {
      e.ToTable("refresh_token");
      e.HasKey(x => x.Id);
      e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
      e.HasIndex(x => x.TokenHash).IsUnique();
      e.HasIndex(x => x.UserId);
      e.HasIndex(x => x.Family);
      e.Property(x => x.ReplacedByTokenHash).HasMaxLength(64);
      e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    });

    modelBuilder.Entity<AuditLog>(e =>
    {
      e.ToTable("audit_log");
      e.HasKey(x => x.Id);
      e.Property(x => x.Action).HasMaxLength(64).IsRequired();
      e.Property(x => x.TargetType).HasMaxLength(64);
      e.Property(x => x.TargetId).HasMaxLength(64);
      e.Property(x => x.IpAddress).HasMaxLength(64);
      e.Property(x => x.Details).HasColumnType("jsonb");
      e.HasIndex(x => x.OccurredAt);
      e.HasIndex(x => x.ActorUserId);
    });

    ApplySnakeCaseNames(modelBuilder);
  }

  // Package 2 (Collect): catalogue, sources, samples, workouts, check-ins, daily summaries.
  // Mirrors 0003_collect.up.sql. The sample table is partitioned by month on start_at there;
  // EF only needs id as its key (the database key is (id, start_at)).
  private void ConfigureCollect(ModelBuilder modelBuilder)
  {
    modelBuilder.Entity<Metric>(e =>
    {
      e.ToTable("metric");
      e.HasKey(x => x.Key);
      e.Property(x => x.Key).HasMaxLength(64);
      e.Property(x => x.Unit).HasMaxLength(32).IsRequired();
      e.Property(x => x.Kind).HasMaxLength(16).IsRequired();
      e.Property(x => x.Category).HasConversion(LowercaseEnum<ShareCategory>()).HasMaxLength(16);
      e.Property(x => x.DisplayName).HasMaxLength(80).IsRequired();
      e.Property(x => x.Blurb).IsRequired();
      e.Property(x => x.GoodDirection).HasMaxLength(16).IsRequired();
      e.Property(x => x.Loinc).HasMaxLength(16);
      e.Property(x => x.ValueLabels).HasColumnType("jsonb");
      e.Property(x => x.SexApplicability).HasMaxLength(8).IsRequired();
      e.Ignore(x => x.IsLab);
    });

    modelBuilder.Entity<Source>(e =>
    {
      e.ToTable("source");
      e.HasKey(x => x.Id);
      e.Property(x => x.Kind).HasMaxLength(16).IsRequired();
      e.Property(x => x.DeviceName).HasMaxLength(80).IsRequired();
      e.Property(x => x.Platform).HasMaxLength(32).IsRequired();
      e.Property(x => x.AppVersion).HasMaxLength(32);
      e.HasIndex(x => x.PersonId);
      e.HasIndex(x => new { x.PersonId, x.Kind, x.DeviceName, x.Platform }).IsUnique().HasFilter("deleted_at IS NULL");
      e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Cascade);
      e.Ignore(x => x.IsDeleted);
    });

    modelBuilder.Entity<Sample>(e =>
    {
      e.ToTable("sample");
      e.HasKey(x => x.Id);
      e.Property(x => x.SourceUid).HasMaxLength(128).IsRequired();
      e.Property(x => x.MetricKey).HasMaxLength(64).IsRequired();
      e.Property(x => x.Unit).HasMaxLength(32).IsRequired();
      e.Property(x => x.Meta).HasColumnType("jsonb");
      e.HasIndex(x => new { x.PersonId, x.MetricKey, x.StartAt }).IsDescending(false, false, true);
      e.HasIndex(x => new { x.SourceId, x.SourceUid, x.StartAt }).IsUnique();
    });

    modelBuilder.Entity<Workout>(e =>
    {
      e.ToTable("workout");
      e.HasKey(x => x.Id);
      e.Property(x => x.SourceUid).HasMaxLength(128).IsRequired();
      e.Property(x => x.Type).HasMaxLength(64).IsRequired();
      e.Property(x => x.Meta).HasColumnType("jsonb");
      e.HasIndex(x => new { x.SourceId, x.SourceUid }).IsUnique();
      e.HasIndex(x => new { x.PersonId, x.StartAt }).IsDescending(false, true);
      e.HasOne<Source>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
    });

    modelBuilder.Entity<Checkin>(e =>
    {
      e.ToTable("checkin");
      e.HasKey(x => x.Id);
      e.Property(x => x.Tags).HasColumnType("text[]");
      e.HasIndex(x => new { x.PersonId, x.At }).IsDescending(false, true);
      e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Cascade);
    });

    modelBuilder.Entity<DailySummary>(e =>
    {
      e.ToTable("daily_summary");
      e.HasKey(x => new { x.PersonId, x.MetricKey, x.Day });
      e.Property(x => x.MetricKey).HasMaxLength(64);
    });
  }

  // Package 3 (Interpret): baselines, insight cards, model call log, Today cache. Mirrors 0005_interpret.up.sql.
  private void ConfigureInterpret(ModelBuilder modelBuilder)
  {
    modelBuilder.Entity<Baseline>(e =>
    {
      e.ToTable("baseline");
      e.HasKey(x => new { x.PersonId, x.MetricKey, x.ComputedFor });
      e.Property(x => x.MetricKey).HasMaxLength(64);
      e.HasIndex(x => new { x.PersonId, x.ComputedFor }).IsDescending(false, true);
      e.Ignore(x => x.IsEstablished);
    });

    modelBuilder.Entity<Insight>(e =>
    {
      e.ToTable("insight");
      e.HasKey(x => x.Id);
      e.Property(x => x.Kind).HasMaxLength(32).IsRequired();
      e.Property(x => x.Severity).HasMaxLength(16).IsRequired();
      e.Property(x => x.Metrics).HasColumnType("text[]");
      e.Property(x => x.Evidence).HasColumnType("jsonb").IsRequired();
      e.Property(x => x.Templated).IsRequired();
      e.Property(x => x.Model).HasMaxLength(80);
      e.Property(x => x.Reaction).HasMaxLength(16);
      e.HasIndex(x => new { x.PersonId, x.Kind, x.Day }).IsUnique();
      e.HasIndex(x => new { x.PersonId, x.Day }).IsDescending(false, true);
      e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Cascade);
      e.Ignore(x => x.Text);
    });

    modelBuilder.Entity<AiCall>(e =>
    {
      e.ToTable("ai_call");
      e.HasKey(x => x.Id);
      e.Property(x => x.Purpose).HasMaxLength(32).IsRequired();
      e.Property(x => x.PromptHash).HasMaxLength(64).IsRequired();
      e.HasIndex(x => new { x.PersonId, x.CreatedAt }).IsDescending(false, true);
    });

    modelBuilder.Entity<TodayCache>(e =>
    {
      e.ToTable("today_cache");
      e.HasKey(x => x.PersonId);
      e.Property(x => x.Sentence).IsRequired();
      e.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
    });
  }

  // Package 4 (Labs): reports, results, marker aliases, reference ranges. Mirrors 0006_labs.up.sql.
  private static void ConfigureConnect(ModelBuilder modelBuilder)
  {
    modelBuilder.Entity<OAuthClient>(e =>
    {
      e.ToTable("oauth_client");
      e.HasKey(x => x.ClientId);
      e.Property(x => x.ClientId).HasMaxLength(512);
      e.Property(x => x.Kind).HasMaxLength(16).IsRequired();
      e.Property(x => x.ClientName).HasMaxLength(200).IsRequired();
      e.Property(x => x.ClientUri).HasMaxLength(512);
      e.Property(x => x.LogoUri).HasMaxLength(512);
      e.Property(x => x.RedirectUris).IsRequired();
      e.Property(x => x.TokenEndpointAuthMethod).HasMaxLength(32).IsRequired();
      e.Property(x => x.SecretHash).HasMaxLength(64);
    });

    modelBuilder.Entity<OAuthAuthorizationRequest>(e =>
    {
      e.ToTable("oauth_authorization_request");
      e.HasKey(x => x.Id);
      e.Property(x => x.ClientId).HasMaxLength(512).IsRequired();
      e.Property(x => x.RedirectUri).HasMaxLength(2048).IsRequired();
      e.Property(x => x.Scope).HasMaxLength(256).IsRequired();
      e.Property(x => x.State).HasMaxLength(1024);
      e.Property(x => x.CodeChallenge).HasMaxLength(128).IsRequired();
      e.Property(x => x.Resource).HasMaxLength(512);
      e.Property(x => x.CodeHash).HasMaxLength(64);
      e.HasIndex(x => x.CodeHash).IsUnique().HasFilter("code_hash IS NOT NULL");
      e.HasOne<OAuthClient>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
      e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    });

    modelBuilder.Entity<OAuthRefreshToken>(e =>
    {
      e.ToTable("oauth_refresh_token");
      e.HasKey(x => x.Id);
      e.Property(x => x.ClientId).HasMaxLength(512).IsRequired();
      e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
      e.Property(x => x.Scope).HasMaxLength(256).IsRequired();
      e.Property(x => x.Resource).HasMaxLength(512);
      e.HasIndex(x => x.TokenHash).IsUnique();
      e.HasIndex(x => new { x.UserId, x.ClientId });
      e.HasOne<OAuthClient>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
      e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    });

    modelBuilder.Entity<PersonalAccessToken>(e =>
    {
      e.ToTable("personal_access_token");
      e.HasKey(x => x.Id);
      e.Property(x => x.Name).HasMaxLength(80).IsRequired();
      e.Property(x => x.Prefix).HasMaxLength(16).IsRequired();
      e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
      e.Property(x => x.Scope).HasMaxLength(256).IsRequired();
      e.HasIndex(x => x.TokenHash).IsUnique();
      e.HasIndex(x => x.UserId);
      e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    });

    modelBuilder.Entity<AiMemory>(e =>
    {
      e.ToTable("ai_memory");
      e.HasKey(x => x.Id);
      e.Property(x => x.MemoryType).HasMaxLength(16).IsRequired();
      e.Property(x => x.MemoryKey).HasMaxLength(80).IsRequired();
      e.Property(x => x.ValueCipher).IsRequired();
      e.Property(x => x.DisplayText).HasMaxLength(200);
      e.Property(x => x.Source).HasMaxLength(200);
      e.HasIndex(x => new { x.PersonId, x.MemoryType, x.MemoryKey }).IsUnique();
      e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Cascade);
    });

    modelBuilder.Entity<OuraConnection>(e =>
    {
      e.ToTable("oura_connection");
      e.HasKey(x => x.PersonId);
      e.Property(x => x.AccessCipher).IsRequired();
      e.Property(x => x.RefreshCipher).IsRequired();
      e.Property(x => x.Scope).HasMaxLength(256);
      e.Property(x => x.LastError).HasMaxLength(500);
      e.HasIndex(x => x.NextPullAt).HasFilter("next_pull_at IS NOT NULL");
      e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Cascade);
      e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
      e.HasOne<Source>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Cascade);
    });

    modelBuilder.Entity<OuraAuthState>(e =>
    {
      e.ToTable("oura_auth_state");
      e.HasKey(x => x.StateHash);
      e.Property(x => x.StateHash).HasMaxLength(64);
      e.Property(x => x.VerifierCipher).IsRequired();
      e.Property(x => x.ReturnPath).HasMaxLength(32);
      e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    });
  }

  private static void ConfigureSso(ModelBuilder modelBuilder)
  {
    modelBuilder.Entity<UserIdentity>(e =>
    {
      e.ToTable("user_identity");
      e.HasKey(x => x.Id);
      e.Property(x => x.Issuer).HasMaxLength(500).IsRequired();
      e.Property(x => x.Subject).HasMaxLength(255).IsRequired();
      e.Property(x => x.Email).HasMaxLength(254);
      e.HasIndex(x => new { x.Issuer, x.Subject }).IsUnique();
      e.HasIndex(x => new { x.UserId, x.Issuer }).IsUnique();
      e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    });

    modelBuilder.Entity<SsoAuthState>(e =>
    {
      e.ToTable("sso_auth_state");
      e.HasKey(x => x.StateHash);
      e.Property(x => x.StateHash).HasMaxLength(64);
      e.Property(x => x.Nonce).HasMaxLength(100).IsRequired();
      e.Property(x => x.VerifierCipher).IsRequired();
      e.Property(x => x.Intent).HasMaxLength(16).IsRequired();
      e.Property(x => x.Client).HasMaxLength(16).IsRequired();
      e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    });

    modelBuilder.Entity<SsoHandoff>(e =>
    {
      e.ToTable("sso_handoff");
      e.HasKey(x => x.CodeHash);
      e.Property(x => x.CodeHash).HasMaxLength(64);
      e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    });
  }

  private void ConfigureLabs(ModelBuilder modelBuilder)
  {
    modelBuilder.Entity<LabReport>(e =>
    {
      e.ToTable("lab_report");
      e.HasKey(x => x.Id);
      e.Property(x => x.Provider).HasMaxLength(16).IsRequired();
      e.Property(x => x.ImageSha256).HasMaxLength(64);
      e.HasIndex(x => new { x.PersonId, x.CollectedOn }).IsDescending(false, true);
      e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Cascade);
      e.HasMany(x => x.Results).WithOne().HasForeignKey(r => r.ReportId).OnDelete(DeleteBehavior.Cascade);
    });

    modelBuilder.Entity<LabResult>(e =>
    {
      e.ToTable("lab_result");
      e.HasKey(x => x.Id);
      e.Property(x => x.MetricKey).HasMaxLength(64).IsRequired();
      e.Property(x => x.Unit).HasMaxLength(32).IsRequired();
      e.Property(x => x.Flag).HasMaxLength(8).IsRequired();
      e.HasIndex(x => new { x.ReportId, x.MetricKey }).IsUnique();
      e.HasIndex(x => new { x.PersonId, x.MetricKey });
    });

    modelBuilder.Entity<LabMarkerAlias>(e =>
    {
      e.ToTable("lab_marker_alias");
      e.HasKey(x => new { x.MetricKey, x.Alias });
      e.Property(x => x.MetricKey).HasMaxLength(64);
      e.Property(x => x.Alias).HasMaxLength(80);
    });

    modelBuilder.Entity<LabReference>(e =>
    {
      e.ToTable("lab_reference");
      e.HasKey(x => new { x.MetricKey, x.Sex });
      e.Property(x => x.MetricKey).HasMaxLength(64);
      e.Property(x => x.Sex).HasMaxLength(8);
      e.Property(x => x.GoodDirection).HasMaxLength(16).IsRequired();
      e.Property(x => x.Tier).HasMaxLength(16).IsRequired();
      e.Property(x => x.SourceUrl).IsRequired();
      e.Ignore(x => x.HasRange);
    });
  }

  /// <summary>Enums are stored as their lowercase name, the same spelling the API uses.</summary>
  private static ValueConverter<TEnum, string> LowercaseEnum<TEnum>() where TEnum : struct, Enum =>
    new(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<TEnum>(v, ignoreCase: true));

  private static void ApplySnakeCaseNames(ModelBuilder modelBuilder)
  {
    foreach (var entity in modelBuilder.Model.GetEntityTypes())
    {
      foreach (var property in entity.GetProperties())
      {
        property.SetColumnName(ToSnakeCase(property.Name));
      }
      foreach (var key in entity.GetKeys())
      {
        key.SetName(ToSnakeCase($"pk_{entity.GetTableName()}"));
      }
      foreach (var fk in entity.GetForeignKeys())
      {
        fk.SetConstraintName(ToSnakeCase($"fk_{entity.GetTableName()}_{string.Join("_", fk.Properties.Select(p => p.Name))}"));
      }
      foreach (var index in entity.GetIndexes())
      {
        index.SetDatabaseName(ToSnakeCase($"ix_{entity.GetTableName()}_{string.Join("_", index.Properties.Select(p => p.Name))}"));
      }
    }
  }

  private static readonly Regex UpperRun = new("(?<=[a-z0-9])([A-Z])|(?<=[A-Z])([A-Z][a-z])", RegexOptions.Compiled);

  public static string ToSnakeCase(string name) => UpperRun.Replace(name, "_$1$2").ToLowerInvariant().Replace("__", "_");
}
