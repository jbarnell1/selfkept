using System.Net;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace SelfKept.IntegrationTests;

/// <summary>
/// Labs on real Postgres: the migrated tables, a confirmed report, one lab.* sample per result in
/// the partitioned sample table, the derived ratios, and the cascade on delete.
/// </summary>
public class PostgresLabsTests
{
  [PostgresFact]
  public async Task CreateReport_WritesResultsDerivedRatiosAndSamples_AndDeleteRemovesThem()
  {
    await using var factory = new ApiFactory();
    factory.UsesPostgres.Should().BeTrue();
    var client = factory.CreateClient();
    var registered = await client.PostJsonAsync("/api/auth/register", new { email = "pg-labs@example.com", password = "correct horse battery staple", displayName = "Pg" });
    registered.StatusCode.Should().Be(HttpStatusCode.OK, await registered.Content.ReadAsStringAsync());
    var auth = await registered.ReadJsonAsync();
    client.UseBearer(auth.GetProperty("accessToken").GetString()!);
    var personId = auth.GetProperty("user").GetProperty("person").GetProperty("id").GetGuid();

    var parsed = await (await client.PostJsonAsync("/api/labs/parse", new { text = LabsFlowTests.RythmBlock })).ReadJsonAsync();
    var results = parsed.GetProperty("candidates").EnumerateArray()
      .Select(c => new { metricKey = c.GetProperty("metricKey").GetString(), value = c.GetProperty("value").GetDouble(), unit = c.GetProperty("unit").GetString() })
      .ToList();
    results.Should().HaveCount(26);

    var created = await client.PostJsonAsync("/api/labs/reports", new { provider = "rythm", collectedOn = "2026-10-20", results, image = Convert.ToBase64String(new byte[] { 1, 2, 3 }) });
    created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
    var reportId = (await created.ReadJsonAsync()).GetProperty("id").GetGuid();

    await using var connection = new NpgsqlConnection(factory.ScratchConnection);
    await connection.OpenAsync();

    await using (var reference = new NpgsqlCommand("SELECT count(*) FROM lab_reference", connection))
    {
      (await reference.ExecuteScalarAsync()).Should().Be(44L, "0007 seeded the CSV rows");
    }
    await using (var rows = new NpgsqlCommand("SELECT metric_key, value, unit, derived FROM lab_result WHERE report_id = @r ORDER BY metric_key", connection))
    {
      rows.Parameters.AddWithValue("r", reportId);
      var list = new List<(string Key, double Value, string Unit, bool Derived)>();
      await using var reader = await rows.ExecuteReaderAsync();
      while (await reader.ReadAsync()) list.Add((reader.GetString(0), reader.GetDouble(1), reader.GetString(2), reader.GetBoolean(3)));
      list.Should().HaveCount(29);
      list.Where(r => r.Derived).Select(r => r.Key).Should().BeEquivalentTo(new[] { "lab.ldl_apob_ratio", "lab.tc_hdl_ratio", "lab.tg_hdl_ratio" });
      list.Single(r => r.Key == "lab.ldl_apob_ratio").Value.Should().Be(0.94);
      list.Single(r => r.Key == "lab.tc_hdl_ratio").Value.Should().Be(4.42);
      list.Single(r => r.Key == "lab.tg_hdl_ratio").Value.Should().Be(3.44);
      list.Should().OnlyContain(r => r.Derived ? r.Unit == "ratio" : true);
    }
    await using (var report = new NpgsqlCommand("SELECT provider, collected_on, source_image_cipher IS NOT NULL, image_sha256, confirmed_at IS NOT NULL FROM lab_report WHERE id = @r", connection))
    {
      report.Parameters.AddWithValue("r", reportId);
      await using var reader = await report.ExecuteReaderAsync();
      (await reader.ReadAsync()).Should().BeTrue();
      reader.GetString(0).Should().Be("rythm");
      reader.GetFieldValue<DateOnly>(1).Should().Be(new DateOnly(2026, 10, 20));
      reader.GetBoolean(2).Should().BeTrue("the image is stored, encrypted");
      reader.GetString(3).Should().HaveLength(64);
      reader.GetBoolean(4).Should().BeTrue();
    }
    await using (var samples = new NpgsqlCommand("SELECT s.metric_key, s.value, s.start_at, s.meta::text, src.kind, src.device_name FROM sample s JOIN source src ON src.id = s.source_id WHERE s.person_id = @p AND s.metric_key LIKE 'lab.%' ORDER BY s.metric_key", connection))
    {
      samples.Parameters.AddWithValue("p", personId);
      var list = new List<(string Key, double Value, DateTime Start, string? Meta, string Kind, string Device)>();
      await using var reader = await samples.ExecuteReaderAsync();
      while (await reader.ReadAsync()) list.Add((reader.GetString(0), reader.GetDouble(1), reader.GetDateTime(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), reader.GetString(5)));
      list.Should().HaveCount(29, "one sample per result, derived ratios included");
      list.Should().OnlyContain(s => s.Kind == "lab_scan" && s.Device == "Lab results");
      list.Should().OnlyContain(s => s.Start.ToUniversalTime() == new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc), "noon UTC for a UTC person");
      list.Single(s => s.Key == "lab.apob").Value.Should().Be(140);
      list.Single(s => s.Key == "lab.tg_hdl_ratio").Meta.Should().Contain("derived");
      list.Single(s => s.Key == "lab.apob").Meta.Should().BeNull();
    }
    await using (var exists = new NpgsqlCommand("SELECT to_regclass('sample_202610') IS NOT NULL", connection))
    {
      ((bool)(await exists.ExecuteScalarAsync())!).Should().BeTrue("the month partition was created for the lab samples");
    }
    await using (var audit = new NpgsqlCommand("SELECT count(*) FROM audit_log WHERE action = 'lab.report.created'", connection))
    {
      (await audit.ExecuteScalarAsync()).Should().Be(1L);
    }

    var pdf = await client.GetAsync("/api/labs/summary.pdf?from=2026-08-01&to=2026-10-29");
    pdf.StatusCode.Should().Be(HttpStatusCode.OK);
    pdf.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
    (await pdf.Content.ReadAsByteArrayAsync()).Length.Should().BeGreaterThan(1000);

    (await client.DeleteAsync($"/api/labs/reports/{reportId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    await using (var results2 = new NpgsqlCommand("SELECT count(*) FROM lab_result WHERE report_id = @r", connection))
    {
      results2.Parameters.AddWithValue("r", reportId);
      (await results2.ExecuteScalarAsync()).Should().Be(0L, "results cascade with the report");
    }
    await using (var samples2 = new NpgsqlCommand("SELECT count(*) FROM sample WHERE person_id = @p AND metric_key LIKE 'lab.%'", connection))
    {
      samples2.Parameters.AddWithValue("p", personId);
      (await samples2.ExecuteScalarAsync()).Should().Be(0L, "the samples are removed with the report");
    }
    await using (var audit = new NpgsqlCommand("SELECT count(*) FROM audit_log WHERE action IN ('lab.report.deleted', 'lab.summary.exported')", connection))
    {
      (await audit.ExecuteScalarAsync()).Should().Be(2L);
    }
  }
}
