using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using SelfKept.Core.Entities;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services;
using Xunit;

namespace SelfKept.UnitTests.Services;

public class InviteServiceTests
{
  private readonly Mock<IInviteRepository> _invites = new();
  private readonly Mock<IAuditService> _audit = new();
  private readonly TestClock _clock = new(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero));
  private readonly List<HouseholdInvite> _stored = new();

  public InviteServiceTests()
  {
    _invites.Setup(r => r.AddAsync(It.IsAny<HouseholdInvite>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((HouseholdInvite i, CancellationToken _) => { _stored.Add(i); return i; });
    _invites.Setup(r => r.GetByCodeHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((string hash, CancellationToken _) => _stored.FirstOrDefault(i => i.CodeHash == hash));
    _invites.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((Guid id, CancellationToken _) => _stored.FirstOrDefault(i => i.Id == id));
    _invites.Setup(r => r.GetPendingAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((Guid h, DateTime now, CancellationToken _) =>
        _stored.Where(i => i.HouseholdId == h && i.IsPending(now)).ToList());
    // Mirrors the repository: the first caller spends the code, a second one finds it used.
    _invites.Setup(r => r.TryMarkUsedAsync(It.IsAny<HouseholdInvite>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((HouseholdInvite i, Guid user, DateTime now, CancellationToken _) =>
      {
        if (i.UsedAt != null) return false;
        i.UsedAt = now;
        i.UsedByUserId = user;
        return true;
      });
  }

  private InviteService CreateService(int? validDays = null)
  {
    var config = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?> { ["Household:InviteDays"] = validDays?.ToString() })
      .Build();
    return new InviteService(_invites.Object, _audit.Object, _clock, config);
  }

  [Fact]
  public async Task Create_ReturnsPlainCodeOnce_StoresOnlyHash()
  {
    var householdId = Guid.NewGuid();
    var created = await CreateService().CreateAsync(householdId, Guid.NewGuid(), RequestInfo.None);

    created.Code.Should().MatchRegex("^[A-Z2-9]{5}-[A-Z2-9]{5}-[A-Z2-9]{5}-[A-Z2-9]{5}$");
    created.ExpiresAt.Should().Be(_clock.GetUtcNow().UtcDateTime.AddDays(7));
    _stored.Should().ContainSingle().Which.CodeHash.Should().Be(InviteService.HashCode(created.Code));
    _stored[0].CodeHash.Should().NotContain(created.Code.Replace("-", ""));
    _audit.Verify(a => a.RecordAsync(It.Is<AuditEvent>(e => e.Action == AuditActions.InviteCreated && e.HouseholdId == householdId), It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task FindValid_AcceptsCodeInAnyCaseOrSpacing()
  {
    var created = await CreateService().CreateAsync(Guid.NewGuid(), Guid.NewGuid(), RequestInfo.None);

    (await CreateService().FindValidAsync(created.Code.ToLowerInvariant().Replace("-", " "))).Should().NotBeNull();
    (await CreateService().FindValidAsync("ZZZZZ-ZZZZZ-ZZZZZ-ZZZZZ")).Should().BeNull();
  }

  [Fact]
  public async Task Consume_IsOneTime_SecondUseIsRefused()
  {
    var service = CreateService();
    var created = await service.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), RequestInfo.None);
    var invite = (await service.FindValidAsync(created.Code))!;

    await service.ConsumeAsync(invite, Guid.NewGuid());

    (await service.FindValidAsync(created.Code)).Should().BeNull("a spent code is no longer valid");
    var secondUse = () => service.ConsumeAsync(invite, Guid.NewGuid());
    await secondUse.Should().ThrowAsync<RegistrationClosedException>();
  }

  [Fact]
  public async Task Consume_WhenAnotherRegistrationSpentItFirst_IsRefused()
  {
    var service = CreateService();
    var created = await service.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), RequestInfo.None);
    var invite = (await service.FindValidAsync(created.Code))!;
    _invites.Setup(r => r.TryMarkUsedAsync(invite, It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

    var act = () => service.ConsumeAsync(invite, Guid.NewGuid());

    await act.Should().ThrowAsync<RegistrationClosedException>();
  }

  [Fact]
  public async Task FindValid_ExpiredCode_IsNull()
  {
    var service = CreateService(validDays: 1);
    var created = await service.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), RequestInfo.None);

    (await service.FindValidAsync(created.Code)).Should().NotBeNull();
    _clock.Advance(TimeSpan.FromDays(1) + TimeSpan.FromSeconds(1));
    (await service.FindValidAsync(created.Code)).Should().BeNull();
  }

  [Fact]
  public async Task ListPending_ExcludesUsedAndExpired()
  {
    var householdId = Guid.NewGuid();
    var service = CreateService(validDays: 2);
    var live = await service.CreateAsync(householdId, Guid.NewGuid(), RequestInfo.None);
    var spent = await service.CreateAsync(householdId, Guid.NewGuid(), RequestInfo.None);
    await service.ConsumeAsync((await service.FindValidAsync(spent.Code))!, Guid.NewGuid());
    await CreateService().CreateAsync(Guid.NewGuid(), Guid.NewGuid(), RequestInfo.None); // other household

    var pending = await service.ListPendingAsync(householdId);

    pending.Should().ContainSingle().Which.Id.Should().Be(live.Id);
  }

  [Fact]
  public async Task Revoke_OnlyPendingInvitesOfOwnHousehold()
  {
    var householdId = Guid.NewGuid();
    var service = CreateService();
    var mine = await service.CreateAsync(householdId, Guid.NewGuid(), RequestInfo.None);
    var theirs = await service.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), RequestInfo.None);

    (await service.RevokeAsync(householdId, theirs.Id)).Should().BeFalse();
    (await service.RevokeAsync(householdId, mine.Id)).Should().BeTrue();
    _invites.Verify(r => r.RemoveAsync(It.Is<HouseholdInvite>(i => i.Id == mine.Id), It.IsAny<CancellationToken>()), Times.Once);
  }
}
