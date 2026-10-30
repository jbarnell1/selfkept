using System.ComponentModel.DataAnnotations;

namespace SelfKept.Core.DTOs;

// Wire shapes for docs/sso-design.md.

/// <summary>Whether this server offers provider sign-in, and the name to show on the button.</summary>
public sealed record SsoInfoDto(bool Enabled, string? Name);

/// <summary>The signed-in account's provider link.</summary>
public sealed record SsoLinkStatusDto(bool Enabled, string? Name, bool Linked, string? Email, DateTime? LinkedAt);

/// <summary>Where the app sends the browser to start a link trip.</summary>
public sealed record SsoStartDto(string Url);

/// <summary>The phone trades the handoff code from the app link for a session.</summary>
public sealed record SsoExchangeRequest([Required, MaxLength(200)] string Code);
