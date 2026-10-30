using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SelfKept.Api.Diagnostics;
using SelfKept.Api.Extensions;
using SelfKept.Core.Auth;
using Serilog.Context;

namespace SelfKept.Api.Controllers;

/// <summary>An error the app hit on a phone or in a browser, sent to this server and nowhere else.</summary>
public sealed record ClientErrorReport(
  [Required, MaxLength(32)] string Kind,
  [Required, MaxLength(500)] string Message,
  [MaxLength(4000)] string? Stack,
  [MaxLength(32)] string? AppVersion,
  [MaxLength(32)] string? Platform,
  [MaxLength(200)] string? Where);

/// <summary>The admin's recent warnings and errors, newest first.</summary>
public sealed record RecentProblemsDto(int Last24Hours, IReadOnlyList<ProblemEntry> Items);

/// <summary>
/// Making bugs visible without telemetry: the apps report their own errors to this server, which
/// logs them beside its own, and the admin reads the recent ones in the app.
/// </summary>
public sealed class DiagnosticsController : BaseController
{
  private const int ListLimit = 50;

  private readonly RecentProblems _problems;
  private readonly TimeProvider _clock;
  private readonly ILogger<DiagnosticsController> _logger;

  public DiagnosticsController(RecentProblems problems, TimeProvider clock, ILogger<DiagnosticsController> logger)
  {
    _problems = problems;
    _clock = clock;
    _logger = logger;
  }

  /// <summary>Anonymous on purpose: the app can fail before anyone signs in. Small per-IP limit.</summary>
  [HttpPost("client-errors")]
  [AllowAnonymous]
  [EnableRateLimiting(RateLimitPolicies.ClientErrors)]
  public IActionResult ReportClientError([FromBody] ClientErrorReport report)
  {
    var user = User.FindFirst(TokenClaims.Subject)?.Value ?? "signed out";
    using (LogContext.PushProperty(RecentProblems.DetailProperty, report.Stack))
    {
      _logger.LogWarning(
        "App error ({Kind}) on {Platform} {AppVersion} at {Where}, user {User}: {Message}",
        report.Kind, report.Platform ?? "unknown", report.AppVersion ?? "unknown", report.Where ?? "unknown", user, report.Message);
    }
    return Accepted();
  }

  [HttpGet("problems")]
  [Authorize(Roles = TokenRoles.Admin)]
  public IActionResult Problems()
  {
    var all = _problems.Snapshot();
    var since = _clock.GetUtcNow().UtcDateTime.AddHours(-24);
    return Ok(new RecentProblemsDto(all.Count(p => p.At >= since), all.Take(ListLimit).ToList()));
  }
}
