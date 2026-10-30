using System.Text;
using System.Text.Json;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;

namespace SelfKept.Infrastructure.Services.Collect;

public static class CollectMapping
{
  public static SourceDto ToDto(this Source s) =>
    new(s.Id, s.Kind, s.DeviceName, s.Platform, s.Enabled, s.LastSyncAt, s.LastSampleAt, s.SampleCount, s.Cursor);

  public static MetricDto ToDto(this Metric m) =>
    new(m.Key, m.Unit, m.Kind, m.Category, m.DisplayName, m.Blurb, m.GoodDirection, m.SexApplicability, ParseJson(m.ValueLabels));

  public static SampleDto ToDto(this Sample s) =>
    new(s.Id, s.MetricKey, s.StartAt, s.EndAt, s.Value, s.Unit, s.SourceId, ParseJson(s.Meta));

  public static DailyPointDto ToDto(this DailySummary d) => new(d.Day, d.Value, d.Min, d.Max, d.Count);

  public static CheckinDto ToDto(this Checkin c, string? note) => new(c.Id, c.PersonId, c.At, c.Mood, note, c.Tags);

  /// <summary>Stored JSON text to a detached element the serializer writes verbatim.</summary>
  public static JsonElement? ParseJson(string? json)
  {
    if (string.IsNullOrWhiteSpace(json)) return null;
    using var doc = JsonDocument.Parse(json);
    return doc.RootElement.Clone();
  }

  public static byte[] ToCipherBytes(string cipherText) => Encoding.UTF8.GetBytes(cipherText);

  public static string FromCipherBytes(byte[] cipher) => Encoding.UTF8.GetString(cipher);
}
