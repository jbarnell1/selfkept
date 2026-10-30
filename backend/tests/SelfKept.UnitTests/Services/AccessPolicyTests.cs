using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using SelfKept.Core.Enums;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;
using SelfKept.Infrastructure.Repositories;
using SelfKept.Infrastructure.Services;
using Xunit;

namespace SelfKept.UnitTests.Services;

public class AccessPolicyTests
{
  private readonly Mock<IShareGrantRepository> _grants = new();
  private readonly Guid _viewer = Guid.NewGuid();
  private readonly Guid _target = Guid.NewGuid();

  private ShareGrantAccessPolicy CreatePolicy() => new(_grants.Object);

  [Fact]
  public async Task CanView_NoGrant_Denies()
  {
    _grants.Setup(g => g.ExistsAsync(_target, _viewer, ShareCategory.Sleep, It.IsAny<CancellationToken>())).ReturnsAsync(false);

    (await CreatePolicy().CanViewAsync(_viewer, _target, ShareCategory.Sleep)).Should().BeFalse();
  }

  [Fact]
  public async Task CanView_GrantForCategory_Allows()
  {
    _grants.Setup(g => g.ExistsAsync(_target, _viewer, ShareCategory.Sleep, It.IsAny<CancellationToken>())).ReturnsAsync(true);

    (await CreatePolicy().CanViewAsync(_viewer, _target, ShareCategory.Sleep)).Should().BeTrue();
  }

  [Fact]
  public async Task CanView_GrantForOtherCategory_Denies()
  {
    _grants.Setup(g => g.ExistsAsync(_target, _viewer, ShareCategory.Sleep, It.IsAny<CancellationToken>())).ReturnsAsync(true);

    (await CreatePolicy().CanViewAsync(_viewer, _target, ShareCategory.Labs)).Should().BeFalse();
  }

  [Fact]
  public async Task CanView_GrantInOtherDirection_Denies()
  {
    // viewer shared with target, not the other way round
    _grants.Setup(g => g.ExistsAsync(_viewer, _target, ShareCategory.Sleep, It.IsAny<CancellationToken>())).ReturnsAsync(true);

    (await CreatePolicy().CanViewAsync(_viewer, _target, ShareCategory.Sleep)).Should().BeFalse();
  }

  [Fact]
  public async Task CanView_Self_AlwaysAllowedWithoutLookup()
  {
    foreach (var category in Enum.GetValues<ShareCategory>())
    {
      (await CreatePolicy().CanViewAsync(_viewer, _viewer, category)).Should().BeTrue();
    }
    _grants.Verify(g => g.ExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ShareCategory>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task EnsureCanView_NoGrant_ThrowsForbidden()
  {
    _grants.Setup(g => g.ExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ShareCategory>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

    var act = () => CreatePolicy().EnsureCanViewAsync(_viewer, _target, ShareCategory.Vitals);

    await act.Should().ThrowAsync<ForbiddenException>();
  }
}

/// <summary>The real share_grant repository against the InMemory provider: the policy's data source.</summary>
public class ShareGrantRepositoryTests
{
  private static SelfKeptDbContext NewContext() => new(
    new DbContextOptionsBuilder<SelfKeptDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

  [Fact]
  public async Task Exists_OtherMemberWithoutGrant_IsFalse()
  {
    await using var context = NewContext();
    var repo = new ShareGrantRepository(context, TimeProvider.System);
    var owner = Guid.NewGuid();
    var friend = Guid.NewGuid();
    var stranger = Guid.NewGuid();
    await repo.ReplaceAsync(Guid.NewGuid(), owner, friend, new[] { ShareCategory.Activity }, CancellationToken.None);

    (await repo.ExistsAsync(owner, friend, ShareCategory.Activity)).Should().BeTrue();
    (await repo.ExistsAsync(owner, stranger, ShareCategory.Activity)).Should().BeFalse();
    (await repo.ExistsAsync(owner, friend, ShareCategory.Sleep)).Should().BeFalse();
  }

  [Fact]
  public async Task Replace_RemovesRevokedAndAddsNew_WithoutDuplicates()
  {
    await using var context = NewContext();
    var repo = new ShareGrantRepository(context, TimeProvider.System);
    var householdId = Guid.NewGuid();
    var owner = Guid.NewGuid();
    var viewer = Guid.NewGuid();

    await repo.ReplaceAsync(householdId, owner, viewer, new[] { ShareCategory.Activity, ShareCategory.Sleep }, CancellationToken.None);
    await repo.ReplaceAsync(householdId, owner, viewer, new[] { ShareCategory.Sleep, ShareCategory.Sleep, ShareCategory.Labs }, CancellationToken.None);

    var grants = await repo.GetByOwnerAsync(owner);
    grants.Select(g => g.Category).Should().BeEquivalentTo(new[] { ShareCategory.Sleep, ShareCategory.Labs });
    (await repo.GetByViewerAsync(viewer)).Should().HaveCount(2);

    await repo.ReplaceAsync(householdId, owner, viewer, Array.Empty<ShareCategory>(), CancellationToken.None);
    (await repo.GetByOwnerAsync(owner)).Should().BeEmpty();
  }
}
