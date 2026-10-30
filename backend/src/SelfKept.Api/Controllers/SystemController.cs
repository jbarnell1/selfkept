using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SelfKept.Api.Extensions;
using SelfKept.Core.Configuration;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;
using SelfKept.Infrastructure.Services.Connect;

namespace SelfKept.Api.Controllers;

/// <summary>Anonymous probes the client uses before anyone signs in.</summary>
[AllowAnonymous]
public sealed class SystemController : BaseController
{
  /// <summary>What this server can do, so the client hides what would only fail.</summary>
  private static readonly string[] Features =
    ["auth.password", "household.invites", "household.sharing", "ingest.batch", "checkins", "data.daily", "data.samples", "insights.rules", "labs.reports", "labs.pdf", "setup.guided", "sources.oura"];

  /// <summary>Added when a local model is configured (Ai:Enabled with a base URL and a model name).</summary>
  private const string ModelFeature = "insights.model";

  private readonly BrandOptions _brand;
  private readonly IConfiguration _configuration;
  private readonly IAuthService _authService;
  private readonly SelfKeptDbContext _dbContext;
  private readonly IChatModel _model;
  private readonly McpSettings _mcp;
  private readonly ILogger<SystemController> _logger;

  public SystemController(
    BrandOptions brand,
    IConfiguration configuration,
    IAuthService authService,
    SelfKeptDbContext dbContext,
    IChatModel model,
    McpSettings mcp,
    ILogger<SystemController> logger)
  {
    _brand = brand;
    _configuration = configuration;
    _authService = authService;
    _dbContext = dbContext;
    _model = model;
    _mcp = mcp;
    _logger = logger;
  }

  [HttpGet("version")]
  public IActionResult GetVersion() => Ok(new
  {
    version = AppVersion.Current,
    minClientVersion = _configuration["System:MinClientVersion"] is { Length: > 0 } min ? min : "0.1.0",
    brand = new
    {
      productName = _brand.ProductName,
      shortName = _brand.ShortName,
      tagline = _brand.Tagline,
      supportUrl = _brand.SupportUrl,
      privacyUrl = _brand.PrivacyUrl,
    },
    features = Features
      .Concat(_model.Enabled ? [ModelFeature] : [])
      .Concat(_mcp.IsConfigured ? ["connect.mcp"] : []),
  });

  [HttpGet("setup-status")]
  public async Task<IActionResult> GetSetupStatus(CancellationToken ct) =>
    Ok(await _authService.GetSetupStatusAsync(ct));

  /// <summary>200 when the database answers, 503 otherwise.</summary>
  [HttpGet("health")]
  public async Task<IActionResult> GetHealth(CancellationToken ct)
  {
    bool databaseOk;
    try
    {
      databaseOk = await _dbContext.Database.CanConnectAsync(ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      _logger.LogWarning(ex, "Health check: database unreachable");
      databaseOk = false;
    }

    var body = new { status = databaseOk ? "ok" : "degraded", database = databaseOk ? "ok" : "unavailable" };
    return databaseOk ? Ok(body) : StatusCode(StatusCodes.Status503ServiceUnavailable, body);
  }
}
