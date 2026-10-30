namespace SelfKept.Core.Configuration;

/// <summary>
/// The product identity, bound from the "Brand" configuration section. The values come from
/// brand/brand.yaml by way of appsettings.Brand.json; nothing else in the backend may hard-code
/// a product name, URL or codename. Use this wherever a name appears (OpenAPI title, version
/// endpoint, log banner, cookie names).
/// </summary>
public sealed record BrandOptions
{
  public const string SectionName = "Brand";

  public string ProductName { get; init; } = "App";
  public string ShortName { get; init; } = "App";
  public string Tagline { get; init; } = string.Empty;
  public string SupportUrl { get; init; } = string.Empty;
  public string PrivacyUrl { get; init; } = string.Empty;
  public string DocsUrl { get; init; } = string.Empty;
  public string FromEmail { get; init; } = string.Empty;

  /// <summary>Lowercase slug used in paths, cookie names, env prefix and image names.</summary>
  public string Codename { get; init; } = "app";

  /// <summary>The refresh-token cookie, "&lt;codename&gt;_refresh" per the API contract.</summary>
  public string RefreshCookieName => $"{Codename}_refresh";
}
