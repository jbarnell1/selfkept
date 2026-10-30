using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SelfKept.Core.Entities;
using SelfKept.Infrastructure.Data;
using SelfKept.Infrastructure.Services;
using Xunit;

namespace SelfKept.IntegrationTests;

/// <summary>
/// Housekeeping removes only rows nobody can use: expired tokens and requests past the grace
/// period, and invites that expired unused a month ago. Live rows, used invites and the people
/// themselves stay. Runs on InMemory or, with SELFKEPT_TEST_CONNECTION, on Postgres.
/// </summary>
public class HousekeepingTests
{
  [Fact]
  public async Task Run_RemovesOnlyWhatHasExpired()
  {
    await using var factory = new ApiFactory();
    var client = factory.CreateClient();
    var registered = await client.PostJsonAsync("/api/auth/register", new { email = "keep@example.com", password = "correct horse battery staple", displayName = "Keep" });
    registered.StatusCode.Should().Be(HttpStatusCode.OK, await registered.Content.ReadAsStringAsync());

    var now = DateTime.UtcNow;
    var old = now - Housekeeping.Grace - TimeSpan.FromHours(1);
    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<SelfKeptDbContext>();
      var user = await db.Users.SingleAsync(u => u.Email == "keep@example.com");
      var householdId = user.HouseholdId!.Value;
      db.RefreshTokens.Add(new RefreshToken { Id = Guid.NewGuid(), UserId = user.Id, TokenHash = "old-refresh", Family = Guid.NewGuid(), CreatedAt = old, ExpiresAt = old });
      db.OAuthClients.Add(new OAuthClient { ClientId = "keep-client", ClientName = "Keep", CreatedAt = now });
      db.OAuthAuthorizationRequests.Add(new OAuthAuthorizationRequest { Id = Guid.NewGuid(), ClientId = "keep-client", RedirectUri = "https://x.example/cb", CodeChallenge = "c", CreatedAt = old, ExpiresAt = old });
      db.OAuthAuthorizationRequests.Add(new OAuthAuthorizationRequest { Id = Guid.NewGuid(), ClientId = "keep-client", RedirectUri = "https://x.example/cb", CodeChallenge = "c", CreatedAt = now, ExpiresAt = now.AddMinutes(10) });
      db.OAuthRefreshTokens.Add(new OAuthRefreshToken { Id = Guid.NewGuid(), UserId = user.Id, ClientId = "keep-client", TokenHash = "old-oauth", CreatedAt = old, ExpiresAt = old });
      db.OAuthRefreshTokens.Add(new OAuthRefreshToken { Id = Guid.NewGuid(), UserId = user.Id, ClientId = "keep-client", TokenHash = "live-oauth", CreatedAt = now, ExpiresAt = now.AddDays(60) });
      db.HouseholdInvites.Add(new HouseholdInvite { Id = Guid.NewGuid(), HouseholdId = householdId, CodeHash = "stale", CreatedByUserId = user.Id, CreatedAt = now.AddDays(-40), ExpiresAt = now.AddDays(-33) });
      db.HouseholdInvites.Add(new HouseholdInvite { Id = Guid.NewGuid(), HouseholdId = householdId, CodeHash = "recent", CreatedByUserId = user.Id, CreatedAt = now.AddDays(-3), ExpiresAt = now.AddDays(-1) });
      db.HouseholdInvites.Add(new HouseholdInvite { Id = Guid.NewGuid(), HouseholdId = householdId, CodeHash = "used", CreatedByUserId = user.Id, CreatedAt = now.AddDays(-40), ExpiresAt = now.AddDays(-33), UsedByUserId = user.Id, UsedAt = now.AddDays(-39) });
      db.OuraAuthStates.Add(new OuraAuthState { StateHash = "old-state", UserId = user.Id, PersonId = user.PersonId, VerifierCipher = "x", CreatedAt = old, ExpiresAt = old });
      db.SsoAuthStates.Add(new SsoAuthState { StateHash = "old-sso-state", Nonce = "n", VerifierCipher = "x", CreatedAt = old, ExpiresAt = old });
      db.SsoHandoffs.Add(new SsoHandoff { CodeHash = "old-handoff", UserId = user.Id, CreatedAt = old, ExpiresAt = old });
      await db.SaveChangesAsync();
    }

    HousekeepingResult result;
    await using (var scope = factory.Services.CreateAsyncScope())
      result = await scope.ServiceProvider.GetRequiredService<Housekeeping>().RunAsync();

    result.Should().Be(new HousekeepingResult(RefreshTokens: 1, OAuthRequests: 1, OAuthRefreshTokens: 1, Invites: 1, OuraStates: 1, SsoStates: 2));

    await using (var scope = factory.Services.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<SelfKeptDbContext>();
      (await db.RefreshTokens.AnyAsync(t => t.TokenHash == "old-refresh")).Should().BeFalse();
      (await db.RefreshTokens.CountAsync()).Should().Be(1, "the session from registering is live");
      (await db.OAuthAuthorizationRequests.CountAsync()).Should().Be(1);
      (await db.OAuthRefreshTokens.Select(t => t.TokenHash).ToListAsync()).Should().Equal("live-oauth");
      (await db.HouseholdInvites.Select(i => i.CodeHash).OrderBy(h => h).ToListAsync()).Should().Equal("recent", "used");
      (await db.Users.CountAsync()).Should().Be(1);
    }

    await using (var scope = factory.Services.CreateAsyncScope())
      (await scope.ServiceProvider.GetRequiredService<Housekeeping>().RunAsync()).Total.Should().Be(0, "a second pass finds nothing");
  }
}
