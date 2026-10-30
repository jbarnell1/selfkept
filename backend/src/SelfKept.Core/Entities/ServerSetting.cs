namespace SelfKept.Core.Entities;

/// <summary>
/// A setting an admin changed from the app rather than the .env file. Secret values are stored
/// encrypted (see ServerSettingStore); a value set in the environment always wins.
/// </summary>
public class ServerSetting
{
  public string Key { get; set; } = string.Empty;
  public string Value { get; set; } = string.Empty;
  public DateTime UpdatedAt { get; set; }
  public Guid? UpdatedBy { get; set; }
}
