using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Infrastructure.Data;
using SelfKept.Infrastructure.Repositories;
using SelfKept.Infrastructure.Services;
using Xunit;

namespace SelfKept.UnitTests.Labs;

/// <summary>
/// The labs share rule on every lab report read, through the real repository, the real
/// share_grant repository and the real policy on the InMemory provider: self ok, member with the
/// labs grant ok, ungranted member null, grant for a different category null.
/// </summary>
public class LabAccessTests : IAsyncLifetime
{
  private readonly SelfKeptDbContext _context = new(
    new DbContextOptionsBuilder<SelfKeptDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
  private readonly Guid _household = Guid.NewGuid();
  private readonly Guid _owner = Guid.NewGuid();
  private readonly Guid _labsViewer = Guid.NewGuid();
  private readonly Guid _activityViewer = Guid.NewGuid();
  private readonly Guid _stranger = Guid.NewGuid();
  private readonly Guid _reportId = Guid.NewGuid();
  private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

  private ShareGrantRepository Grants => new(_context, TimeProvider.System);
  private LabReportRepository Reports => new(_context, new ShareGrantAccessPolicy(Grants));

  public async Task InitializeAsync()
  {
    await Grants.ReplaceAsync(_household, _owner, _labsViewer, [ShareCategory.Labs]);
    await Grants.ReplaceAsync(_household, _owner, _activityViewer, [ShareCategory.Activity]);
    await Reports.AddAsync(new LabReport
    {
      Id = _reportId, PersonId = _owner, Provider = "rythm", CollectedOn = new DateOnly(2026, 10, 20), CreatedAt = Now, ConfirmedAt = Now,
      Results =
      [
        new LabResult { Id = Guid.NewGuid(), ReportId = _reportId, PersonId = _owner, MetricKey = "lab.apob", Value = 92, Unit = "mg/dL", CreatedAt = Now },
        new LabResult { Id = Guid.NewGuid(), ReportId = _reportId, PersonId = _owner, MetricKey = "lab.ldl", Value = 101, Unit = "mg/dL", CreatedAt = Now },
      ],
    });
    await Reports.AddAsync(new LabReport
    {
      Id = Guid.NewGuid(), PersonId = _owner, Provider = "manual", CollectedOn = new DateOnly(2026, 9, 20), CreatedAt = Now.AddDays(-30), ConfirmedAt = Now.AddDays(-30),
      Results = [new LabResult { Id = Guid.NewGuid(), PersonId = _owner, MetricKey = "lab.apob", Value = 88, Unit = "mg/dL", CreatedAt = Now }],
    });
  }

  public Task DisposeAsync() => _context.DisposeAsync().AsTask();

  [Fact]
  public async Task List_Self_ReturnsNewestFirstWithResults()
  {
    var rows = await Reports.ListAsync(_owner, _owner);

    rows.Should().NotBeNull();
    rows!.Should().HaveCount(2);
    rows![0].CollectedOn.Should().Be(new DateOnly(2026, 10, 20));
    rows[0].Results.Should().HaveCount(2);
    rows[0].Results.Select(r => r.MetricKey).Should().Equal("lab.apob", "lab.ldl");
  }

  [Fact]
  public async Task List_MemberWithLabsGrant_ReturnsRows()
  {
    (await Reports.ListAsync(_labsViewer, _owner))!.Should().HaveCount(2);
  }

  [Fact]
  public async Task List_UngrantedMember_IsNull()
  {
    (await Reports.ListAsync(_stranger, _owner)).Should().BeNull();
  }

  [Fact]
  public async Task List_GrantForAnotherCategory_IsNull()
  {
    (await Reports.ListAsync(_activityViewer, _owner)).Should().BeNull("activity was granted, not labs");
  }

  [Fact]
  public async Task Get_ByReportId_FollowsTheOwnersLabsGrant()
  {
    (await Reports.GetAsync(_owner, _reportId))!.Results.Should().HaveCount(2);
    (await Reports.GetAsync(_labsViewer, _reportId)).Should().NotBeNull();
    (await Reports.GetAsync(_activityViewer, _reportId)).Should().BeNull();
    (await Reports.GetAsync(_stranger, _reportId)).Should().BeNull();
    (await Reports.GetAsync(_owner, Guid.NewGuid())).Should().BeNull();
  }

  [Fact]
  public async Task GetOwn_IsTheOwnersOnly_EvenWithAGrant()
  {
    (await Reports.GetOwnAsync(_owner, _reportId)).Should().NotBeNull();
    (await Reports.GetOwnAsync(_labsViewer, _reportId)).Should().BeNull("a grant lets you read, not delete");
  }

  [Fact]
  public async Task ListOwn_LimitsAndOrders_ForTheRules()
  {
    var rows = await Reports.ListOwnAsync(_owner, 1);

    rows.Should().ContainSingle().Which.CollectedOn.Should().Be(new DateOnly(2026, 10, 20));
  }

  [Fact]
  public async Task Remove_DeletesTheResultsToo()
  {
    var report = await Reports.GetOwnAsync(_owner, _reportId);
    await Reports.RemoveAsync(report!);

    (await _context.LabResults.CountAsync(r => r.ReportId == _reportId)).Should().Be(0);
    (await Reports.ListAsync(_owner, _owner))!.Should().HaveCount(1);
  }
}
