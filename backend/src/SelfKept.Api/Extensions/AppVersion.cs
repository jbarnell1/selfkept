namespace SelfKept.Api.Extensions;

/// <summary>The build's product version (flows from backend/Directory.Build.props).</summary>
public static class AppVersion
{
  public static string Current
  {
    get
    {
      var v = typeof(AppVersion).Assembly.GetName().Version;
      return v != null ? $"{v.Major}.{v.Minor}.{v.Build}" : "unknown";
    }
  }
}
