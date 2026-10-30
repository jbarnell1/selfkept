namespace SelfKept.Core.Enums;

/// <summary>Role inside a household. The first account on a server is the admin.</summary>
public enum HouseholdRole
{
  Admin,
  Member,
}

/// <summary>
/// What a person can share with another household member. One grant row per category.
/// Serialized lowercase on the wire and in the database ("activity", "sleep", ...).
/// </summary>
public enum ShareCategory
{
  Activity,
  Sleep,
  Vitals,
  Body,
  Labs,
  Cycle,
  Checkins,
  Insights,
}

public enum SexAtBirth
{
  Unspecified,
  Female,
  Male,
}

public enum UnitSystem
{
  Metric,
  Imperial,
}

/// <summary>
/// One thing a person said they use, in the first-run questions. The setup steps and the
/// "finish setting up" card on Today are built from these. Serialized camelCase on the wire
/// ("appleHealth"), stored by name in user_account.setup_plan.
/// </summary>
public enum SetupItem
{
  AppleHealth,
  HealthConnect,
  Oura,
  Labs,
  Household,
  Assistant,
}

/// <summary>Where an integration's credentials come from.</summary>
public enum CredentialSource
{
  /// <summary>Not set anywhere.</summary>
  None,

  /// <summary>The server's environment (.env). Read-only from the app.</summary>
  Environment,

  /// <summary>Saved from the app by an admin, encrypted in the database.</summary>
  App,
}
