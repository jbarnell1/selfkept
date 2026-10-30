using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Enums;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services;
using Xunit;

namespace SelfKept.UnitTests.Services;

public class AuthServiceTests
{
  private readonly Mock<IUserRepository> _users = new();
  private readonly Mock<IPersonRepository> _persons = new();
  private readonly Mock<IHouseholdRepository> _households = new();
  private readonly Mock<IInviteService> _invites = new();
  private readonly Mock<IPasswordHasher> _hasher = new();
  private readonly Mock<ITokenService> _tokens = new();
  private readonly Mock<IRefreshTokenService> _refresh = new();
  private readonly Mock<IAuditService> _audit = new();
  private readonly Mock<IUnitOfWork> _unitOfWork = new();

  private readonly List<User> _addedUsers = new();
  private readonly List<Household> _addedHouseholds = new();
  private readonly List<HouseholdMember> _addedMembers = new();

  public AuthServiceTests()
  {
    // The unit of work just runs the work; the lock and transaction are Postgres details.
    _unitOfWork
      .Setup(u => u.RunInTransactionAsync(It.IsAny<Func<Task<(User, HouseholdRole, HouseholdInvite?)>>>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
      .Returns((Func<Task<(User, HouseholdRole, HouseholdInvite?)>> work, long? _, CancellationToken _) => work());

    _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns<string>(p => "hash:" + p);
    _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns<string, string>((p, h) => h == "hash:" + p);
    _tokens.Setup(t => t.CreateAccessToken(It.IsAny<User>(), It.IsAny<HouseholdRole>()))
      .Returns(new AccessToken("access", DateTime.UtcNow.AddMinutes(15)));
    _refresh.Setup(r => r.IssueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(new IssuedRefreshToken("refresh", DateTime.UtcNow.AddDays(30), Guid.NewGuid()));

    _persons.Setup(p => p.AddAsync(It.IsAny<Person>(), It.IsAny<CancellationToken>())).ReturnsAsync((Person p, CancellationToken _) => p);
    _users.Setup(u => u.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((User u, CancellationToken _) => { _addedUsers.Add(u); return u; });
    _users.Setup(u => u.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((Guid id, CancellationToken _) => _addedUsers.FirstOrDefault(u => u.Id == id) is { } u ? WithPerson(u) : null);
    _households.Setup(h => h.AddAsync(It.IsAny<Household>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((Household h, CancellationToken _) => { _addedHouseholds.Add(h); return h; });
    _households.Setup(h => h.AddMemberAsync(It.IsAny<HouseholdMember>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((HouseholdMember m, CancellationToken _) => { _addedMembers.Add(m); return m; });
  }

  private static User WithPerson(User u)
  {
    u.Person ??= new Person { Id = u.PersonId, DisplayName = u.DisplayName };
    return u;
  }

  private AuthService CreateService() => new(
    _unitOfWork.Object, _users.Object, _persons.Object, _households.Object, _invites.Object,
    _hasher.Object, _tokens.Object, _refresh.Object, _audit.Object, TimeProvider.System, NullLogger<AuthService>.Instance);

  [Fact]
  public async Task Register_FirstUser_BecomesAdminAndCreatesHousehold()
  {
    _users.Setup(u => u.AnyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
    _users.Setup(u => u.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

    var session = await CreateService().RegisterAsync(
      new RegisterRequest("Ada@Example.com", "correct horse battery", "Ada", null), RequestInfo.None);

    session.User.Role.Should().Be(HouseholdRole.Admin);
    session.User.Email.Should().Be("ada@example.com");
    _addedHouseholds.Should().ContainSingle().Which.Name.Should().Be("Ada's household");
    _addedMembers.Should().ContainSingle().Which.Role.Should().Be(HouseholdRole.Admin);
    session.User.HouseholdId.Should().Be(_addedHouseholds[0].Id);
    _invites.Verify(i => i.FindValidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    _audit.Verify(a => a.RecordAsync(It.Is<AuditEvent>(e => e.Action == AuditActions.Register), It.IsAny<CancellationToken>()), Times.Once);
    _unitOfWork.Verify(u => u.RunInTransactionAsync(
      It.IsAny<Func<Task<(User, HouseholdRole, HouseholdInvite?)>>>(), AuthService.FirstUserLockKey, It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task Register_SecondUser_WithoutInvite_IsRefused()
  {
    _users.Setup(u => u.AnyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
    _users.Setup(u => u.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);
    _invites.Setup(i => i.FindValidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((HouseholdInvite?)null);

    var act = () => CreateService().RegisterAsync(new RegisterRequest("bob@example.com", "correct horse battery", "Bob", null), RequestInfo.None);

    await act.Should().ThrowAsync<RegistrationClosedException>();
    _addedUsers.Should().BeEmpty();
  }

  [Fact]
  public async Task Register_SecondUser_WithValidInvite_JoinsAsMemberAndSpendsInvite()
  {
    var householdId = Guid.NewGuid();
    var invite = new HouseholdInvite { Id = Guid.NewGuid(), HouseholdId = householdId, ExpiresAt = DateTime.UtcNow.AddDays(1) };
    _users.Setup(u => u.AnyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
    _users.Setup(u => u.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);
    _invites.Setup(i => i.FindValidAsync("ABCDE-FGHJK-LMNPQ-RSTUV", It.IsAny<CancellationToken>())).ReturnsAsync(invite);

    var session = await CreateService().RegisterAsync(
      new RegisterRequest("bob@example.com", "correct horse battery", "Bob", "ABCDE-FGHJK-LMNPQ-RSTUV"), RequestInfo.None);

    session.User.Role.Should().Be(HouseholdRole.Member);
    session.User.HouseholdId.Should().Be(householdId);
    _addedHouseholds.Should().BeEmpty();
    _invites.Verify(i => i.ConsumeAsync(invite, _addedUsers.Single().Id, It.IsAny<CancellationToken>()), Times.Once);
    _audit.Verify(a => a.RecordAsync(It.Is<AuditEvent>(e => e.Action == AuditActions.InviteUsed), It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task Register_DuplicateEmail_Conflicts()
  {
    _users.Setup(u => u.GetByEmailAsync("ada@example.com", It.IsAny<CancellationToken>()))
      .ReturnsAsync(new User { Id = Guid.NewGuid(), Email = "ada@example.com" });

    var act = () => CreateService().RegisterAsync(new RegisterRequest("ada@example.com", "correct horse battery", "Ada", null), RequestInfo.None);

    await act.Should().ThrowAsync<ConflictException>();
  }

  [Fact]
  public async Task Login_WrongPassword_ReturnsNullAndAuditsNothing()
  {
    var user = WithPerson(new User { Id = Guid.NewGuid(), Email = "ada@example.com", PasswordHash = "hash:right", DisplayName = "Ada" });
    _users.Setup(u => u.GetByEmailAsync("ada@example.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);

    var session = await CreateService().LoginAsync(new LoginRequest("ada@example.com", "wrong"), RequestInfo.None);

    session.Should().BeNull();
    _refresh.Verify(r => r.IssueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    _audit.Verify(a => a.RecordAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task Login_UnknownEmail_ReturnsNull()
  {
    _users.Setup(u => u.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

    var session = await CreateService().LoginAsync(new LoginRequest("nobody@example.com", "whatever"), RequestInfo.None);

    session.Should().BeNull();
  }

  [Fact]
  public async Task Login_CorrectPassword_IssuesTokensWithMembershipRole()
  {
    var user = WithPerson(new User { Id = Guid.NewGuid(), Email = "ada@example.com", PasswordHash = "hash:right", DisplayName = "Ada", HouseholdId = Guid.NewGuid() });
    _users.Setup(u => u.GetByEmailAsync("ada@example.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);
    _households.Setup(h => h.GetMembershipForUserAsync(user.Id, It.IsAny<CancellationToken>()))
      .ReturnsAsync(new HouseholdMember { UserId = user.Id, Role = HouseholdRole.Admin });

    var session = await CreateService().LoginAsync(new LoginRequest("ADA@example.com", "right"), RequestInfo.None);

    session.Should().NotBeNull();
    session!.User.Role.Should().Be(HouseholdRole.Admin);
    session.Access.Token.Should().Be("access");
    session.RefreshToken.Should().Be("refresh");
    _users.Verify(u => u.UpdateAsync(It.Is<User>(x => x.LastLoginAt != null), It.IsAny<CancellationToken>()), Times.Once);
    _audit.Verify(a => a.RecordAsync(It.Is<AuditEvent>(e => e.Action == AuditActions.Login && e.ActorUserId == user.Id), It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task SetupStatus_ReflectsWhetherAnyUserExists()
  {
    _users.Setup(u => u.AnyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
    (await CreateService().GetSetupStatusAsync()).Should().Be(new SetupStatusDto(true, true));

    _users.Setup(u => u.AnyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
    (await CreateService().GetSetupStatusAsync()).Should().Be(new SetupStatusDto(false, false));
  }
}
