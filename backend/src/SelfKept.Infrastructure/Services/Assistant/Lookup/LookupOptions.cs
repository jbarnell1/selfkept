namespace SelfKept.Infrastructure.Services.Assistant.Lookup;

/// <summary>
/// The Lookup: section (docs/lookup-design.md). Off by default: nothing leaves the server for a
/// cloud model until the person who runs it sets Enabled and their own provider key. The key is
/// never logged, never returned by a route and never shown in the app.
/// </summary>
public sealed class LookupOptions
{
  public const string SectionName = "Lookup";

  /// <summary>The model asked when Model is empty. Never with a date suffix.</summary>
  public const string DefaultModel = "claude-opus-5-5";

  public bool Enabled { get; set; }

  /// <summary>The provider key, from the Anthropic console. A file named Lookup__ApiKey in the secrets folder works too.</summary>
  public string? ApiKey { get; set; }

  /// <summary>Compose passes an unset SELFKEPT_LOOKUP_MODEL as an empty value; <see cref="EffectiveModel"/> falls back then.</summary>
  public string? Model { get; set; } = DefaultModel;

  /// <summary>The most web searches the provider may run for one lookup.</summary>
  public int MaxSearches { get; set; } = 5;

  /// <summary>One attempt, cut off after this long.</summary>
  public int TimeoutSeconds { get; set; } = 180;

  public string EffectiveModel => string.IsNullOrWhiteSpace(Model) ? DefaultModel : Model.Trim();

  public bool IsUsable => Enabled && !string.IsNullOrWhiteSpace(ApiKey);
}
