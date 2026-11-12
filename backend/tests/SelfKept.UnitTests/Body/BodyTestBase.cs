using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;
using SelfKept.Infrastructure.Repositories;
using SelfKept.Infrastructure.Services;
using SelfKept.Infrastructure.Services.Body;
using SelfKept.Infrastructure.Services.Collect;
using SelfKept.UnitTests.Collect;
using Xunit;

namespace SelfKept.UnitTests.Body;

/// <summary>Keeps every audit event so a test can read exactly what was written down.</summary>
public sealed class RecordingAudit : IAuditService
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  public List<AuditEvent> Events { get; } = [];

  public Task RecordAsync(AuditEvent auditEvent, CancellationToken ct = default)
  {
    Events.Add(auditEvent);
    return Task.CompletedTask;
  }

  /// <summary>The details of one event as the audit table would store them.</summary>
  public static string DetailsJson(AuditEvent auditEvent) => JsonSerializer.Serialize(auditEvent.Details, Json);

  /// <summary>Every event, details included, as one string to search for something that must not be there.</summary>
  public string Everything() => string.Join("\n", Events.Select(e => $"{e.Action} {e.TargetType} {e.TargetId} {DetailsJson(e)}"));
}

/// <summary>
/// The body package on the InMemory provider: real repositories, the real share-grant policy, the
/// real encryption service, a clock the test sets and an audit that records. One database per test.
/// </summary>
public abstract class BodyTestBase : IAsyncLifetime
{
  protected static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
  protected static readonly DateOnly Today = new(2026, 11, 10);

  protected readonly SelfKeptDbContext Context = new(
    new DbContextOptionsBuilder<SelfKeptDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
  protected readonly SeedCatalogue Catalogue = new();
  protected readonly TestClock Clock = new(Now);
  protected readonly RecordingAudit Audit = new();
  protected readonly EncryptionService Encryption = new(new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?> { ["Encryption:Key"] = "unit-test-encryption-key-with-plenty-of-entropy-123456" })
    .Build());

  protected readonly Guid Household = Guid.NewGuid();
  protected readonly Person Owner = new() { Id = Guid.NewGuid(), DisplayName = "Ada", TimeZone = "UTC", HeightCm = 180 };
  protected readonly Person Member = new() { Id = Guid.NewGuid(), DisplayName = "Bob", TimeZone = "UTC", Units = UnitSystem.Imperial };
  protected readonly Guid Stranger = Guid.NewGuid();

  private readonly Guid _ownerUser = Guid.NewGuid();
  private readonly Guid _memberUser = Guid.NewGuid();

  protected Actor OwnerActor => new(_ownerUser, Owner.Id, Household, RequestInfo.None);
  protected Actor MemberActor => new(_memberUser, Member.Id, Household, RequestInfo.None);

  protected ShareGrantRepository Grants => new(Context, Clock);
  protected ShareGrantAccessPolicy Policy => new(Grants);
  protected PersonRepository Persons => new(Context);
  protected TodayCacheRepository Cache => new(Context);
  protected EfUnitOfWork UnitOfWork => new(Context);

  protected GoalRepository GoalRepo => new(Context, Policy);
  protected DailyTargetRepository TargetRepo => new(Context, Policy);
  protected JournalRepository JournalRepo => new(Context, Policy);
  protected MedicationRepository MedicationRepo => new(Context, Policy);
  protected StrengthRepository StrengthRepo => new(Context, Policy);
  protected SourceRepository SourceRepo => new(Context);
  protected SampleRepository SampleRepo => new(Context, Policy, Catalogue);
  protected DailySummaryRepository SummaryRepo => new(Context, Policy, Catalogue);

  protected GoalService Goals => new(GoalRepo, Catalogue, Persons, Encryption, Audit, UnitOfWork, Cache, Clock);
  protected TargetService Targets => new(
    TargetRepo, GoalRepo, JournalRepo, StrengthRepo, SummaryRepo, Persons, Catalogue, Encryption, Audit, Cache, Clock);
  protected NutritionSamples Nutrition => new(SourceRepo, SampleRepo, new DailySummaryService(SampleRepo, SummaryRepo, Clock));
  protected JournalService Journal => new(JournalRepo, Persons, Encryption, Audit, Cache, Nutrition, UnitOfWork, Clock);
  protected MedicationService Medications => new(MedicationRepo, Persons, Encryption, Audit, Cache, Clock);
  protected StrengthService Strength => new(StrengthRepo, Persons, Audit, Cache, Clock);
  protected MeasurementService Measurements => new(
    SourceRepo, SampleRepo, Persons, new DailySummaryService(SampleRepo, SummaryRepo, Clock), Catalogue, Audit, UnitOfWork, Cache, Clock);
  protected ProgressService Progress => new(GoalRepo, Targets, StrengthRepo, SummaryRepo, Persons, Catalogue, Encryption, Clock);

  public virtual async Task InitializeAsync()
  {
    Context.Persons.AddRange(Owner, Member);
    await Context.SaveChangesAsync();
  }

  public Task DisposeAsync() => Context.DisposeAsync().AsTask();

  /// <summary>Replaces what the owner shares with the member.</summary>
  protected Task ShareAsync(params ShareCategory[] categories) =>
    Grants.ReplaceAsync(Household, Owner.Id, Member.Id, categories);

  /// <summary>Shares every category with the member except the one named: the wrong grant for that read.</summary>
  protected Task ShareEverythingButAsync(ShareCategory category) =>
    ShareAsync(Enum.GetValues<ShareCategory>().Where(c => c != category).ToArray());

  /// <summary>The call must be refused as a validation failure with exactly this message.</summary>
  protected static async Task ShouldBeRefusedAsync(Func<Task> act, string message)
  {
    var thrown = await act.Should().ThrowAsync<DomainValidationException>();
    thrown.Which.Message.Should().Be(message);
  }

  /// <summary>Writes daily figures straight into the summary table, as ingest would have.</summary>
  protected Task SeedSummariesAsync(Guid personId, string metricKey, params (DateOnly Day, double Value)[] days) =>
    SummaryRepo.UpsertManyAsync(days
      .Select(d => new DailySummary { PersonId = personId, MetricKey = metricKey, Day = d.Day, Value = d.Value, Count = 1, ComputedAt = Now.UtcDateTime })
      .ToList());
}
