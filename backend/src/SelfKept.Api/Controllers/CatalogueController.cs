using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Collect;

namespace SelfKept.Api.Controllers;

/// <summary>The metric catalogue: keys, units, display text and the share category each metric falls under.</summary>
[Authorize]
public sealed class CatalogueController : BaseController
{
  private readonly IMetricCatalogue _catalogue;

  public CatalogueController(IMetricCatalogue catalogue)
  {
    _catalogue = catalogue;
  }

  [HttpGet("metrics")]
  public IActionResult Metrics() => Ok(_catalogue.All.Select(m => m.ToDto()).ToList());
}
