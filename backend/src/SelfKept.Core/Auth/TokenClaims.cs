using SelfKept.Core.Enums;

namespace SelfKept.Core.Auth;

/// <summary>
/// Claim names in every token this server issues: the app's own access tokens, MCP access tokens
/// and personal access tokens. Issued tokens already carry these strings, so they never change.
/// </summary>
public static class TokenClaims
{
  public const string Subject = "sub";
  public const string Email = "email";
  public const string Role = "role";
  public const string Person = "person_id";
  public const string Household = "household_id";
  public const string Scope = "scope";
  public const string ClientId = "client_id";
  public const string ClientName = "client_name";
}

/// <summary>The role claim's values, as <c>[Authorize(Roles = ...)]</c> and <c>IsInRole</c> read them.</summary>
public static class TokenRoles
{
  public const string Admin = "admin";
  public const string Member = "member";

  public static string For(HouseholdRole role) => role == HouseholdRole.Admin ? Admin : Member;
}
