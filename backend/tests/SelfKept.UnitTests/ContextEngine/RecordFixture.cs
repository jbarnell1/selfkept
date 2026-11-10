using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Infrastructure.Data;
using SelfKept.Infrastructure.Repositories;
using SelfKept.Infrastructure.Services;
using SelfKept.Infrastructure.Services.ContextEngine;
using SelfKept.UnitTests.Collect;

namespace SelfKept.UnitTests.ContextEngine;

/// <summary>
/// One person's record on the InMemory provider, read through the real repositories and the real
/// share policy, for the systems, read-back and weekly review services. Today is Tuesday 10 Nov
/// 2026 (UTC); the week before runs Monday 2 to Sunday 8 Nov.
/// </summary>
public sealed class RecordFixture : IAsyncDisposable
{
  public static readonly DateOnly Today = new(2026, 11, 10);
  public static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

  public SelfKeptDbContext Db { get; } = new(new DbContextOptionsBuilder<SelfKeptDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
  public Guid Household { get; } = Guid.NewGuid();
  public Guid Owner { get; } = Guid.NewGuid();
  public Guid Stranger { get; } = Guid.NewGuid();
  public SeedCatalogue Catalogue { get; } = new();
  public TestClock Clock { get; } = new(Now);

  public EncryptionService Encryption { get; } = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
  {
    ["Encryption:Key"] = "unit-test-encryption-key-with-plenty-of-entropy-123456",
  }).Build());

  public ShareGrantRepository Grants => new(Db, TimeProvider.System);
  public ShareGrantAccessPolicy Policy => new(Grants);
  public ReadBackRepository ReadBack => new(Db, Policy);
  public ContextEventRepository Events => new(Db, Policy);
  public PersonRepository Persons => new(Db);

  public SystemsService Systems => new(ReadBack, new BaselineRepository(Db, Policy, Catalogue), new DailySummaryRepository(Db, Policy, Catalogue),
    new LabReportRepository(Db, Policy), new InsightRepository(Db, Policy), Persons, Catalogue, Clock);

  public PatternService Patterns => new(ReadBack, Events, Persons, Clock);

  public ReadBackService ReadBacks => new(ReadBack, new WorkoutRepository(Db, Policy), Persons, Clock);

  public WeeklyReviewService Review => new(ReadBack, new DailyTargetRepository(Db, Policy), new GoalRepository(Db, Policy), new JournalRepository(Db, Policy),
    new StrengthRepository(Db, Policy), new DailySummaryRepository(Db, Policy, Catalogue), new InsightRepository(Db, Policy), Patterns, Persons, Catalogue, Encryption, Clock);

  /// <summary>A member who may see only the given categories.</summary>
  public async Task<Guid> MemberWithAsync(params ShareCategory[] categories)
  {
    var member = Guid.NewGuid();
    await Grants.ReplaceAsync(Household, Owner, member, categories);
    return member;
  }

  public static async Task<RecordFixture> SeededAsync()
  {
    var f = new RecordFixture();
    await f.SeedAsync();
    return f;
  }

  private async Task SeedAsync()
  {
    Db.Persons.Add(new Person { Id = Owner, DisplayName = "Ada", TimeZone = "UTC", CreatedAt = Now, UpdatedAt = Now });
    for (var i = 0; i < 28; i++)
    {
      var day = Today.AddDays(-i);
      Db.DailySummaries.Add(Row(MetricKeys.RestingHrBpm, day, i == 0 ? 66 : 56 + i % 4));
      Db.DailySummaries.Add(Row(MetricKeys.SleepStage, day, 450 + i % 3 * 10));
    }
    Db.DailySummaries.Add(Row(MetricKeys.WeightKg, Today.AddDays(-7), 82.4));
    Db.DailySummaries.Add(Row(MetricKeys.WeightKg, Today.AddDays(-3), 81.9));
    Db.Baselines.Add(new Baseline
    {
      PersonId = Owner, MetricKey = MetricKeys.RestingHrBpm, ComputedFor = Today, Median28 = 57.5, P10_28 = 56, P25_28 = 56.5, P75_28 = 58.5, P90_28 = 59,
      N7 = 7, N28 = 28, ComputedAt = Now,
    });
    var report = Guid.NewGuid();
    Db.LabReports.Add(new LabReport
    {
      Id = report, PersonId = Owner, Provider = LabProviders.Manual, CollectedOn = Today.AddDays(-20), CreatedAt = Now, ConfirmedAt = Now,
      Results =
      [
        new LabResult { Id = Guid.NewGuid(), ReportId = report, PersonId = Owner, MetricKey = "lab.ldl", Value = 160, Unit = "mg/dL", Flag = LabFlags.High, Confirmed = true, CreatedAt = Now },
        new LabResult { Id = Guid.NewGuid(), ReportId = report, PersonId = Owner, MetricKey = "lab.tsh", Value = 2.1, Unit = "mIU/L", Flag = LabFlags.Normal, Confirmed = true, CreatedAt = Now },
      ],
    });
    Db.Insights.Add(new Insight
    {
      Id = Guid.NewGuid(), PersonId = Owner, Day = Today, Kind = InsightKinds.RhrElevated, Severity = InsightSeverities.Watch,
      Metrics = [MetricKeys.RestingHrBpm], Evidence = "{}", Templated = "Resting heart rate is up on your normal.", CreatedAt = Now,
    });
    Db.Insights.Add(new Insight
    {
      Id = Guid.NewGuid(), PersonId = Owner, Day = Today.AddDays(-4), Kind = InsightKinds.SleepGoodWeek, Severity = InsightSeverities.Notice,
      Metrics = [MetricKeys.SleepStage], Evidence = "{}", Templated = "A better week of sleep than your usual.", CreatedAt = Now,
    });
    Db.Goals.Add(new Goal { Id = Guid.NewGuid(), PersonId = Owner, Slot = GoalSlots.Main, Kind = GoalKinds.LoseFat, StartedOn = Today.AddDays(-40), CreatedAt = Now, UpdatedAt = Now });
    Db.DailyTargets.Add(new DailyTarget
    {
      Id = Guid.NewGuid(), PersonId = Owner, Label = "Sleep", Kind = TargetKinds.Metric, MetricKey = MetricKeys.SleepStage, Comparator = TargetComparators.AtLeast,
      Value = 420, Period = TargetPeriods.Day, CreatedAt = Now, UpdatedAt = Now,
    });
    Db.DailyTargets.Add(new DailyTarget
    {
      Id = Guid.NewGuid(), PersonId = Owner, Label = "Lots of sleep", Kind = TargetKinds.Metric, MetricKey = MetricKeys.SleepStage, Comparator = TargetComparators.AtLeast,
      Value = 600, Period = TargetPeriods.Day, SortOrder = 1, CreatedAt = Now, UpdatedAt = Now,
    });
    await Db.SaveChangesAsync();
  }

  private DailySummary Row(string metric, DateOnly day, double value) =>
    new() { PersonId = Owner, MetricKey = metric, Day = day, Value = value, Count = 1, ComputedAt = Now };

  public ValueTask DisposeAsync() => Db.DisposeAsync();
}
