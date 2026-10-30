using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace SelfKept.IntegrationTests;

/// <summary>
/// Package 4 over HTTP: parse a Rythm-style text block, confirm a report from the candidates,
/// read reports and markers, the lab card on Today, the PDF, deletion (samples go too) and the
/// labs share rule on every read. Runs on InMemory or, with SELFKEPT_TEST_CONNECTION set, on a
/// scratch Postgres.
/// </summary>
public class LabsFlowTests
{
  public const string RythmBlock = """
    Rythm Health
    Monthly panel
    Total testosterone 540 ng/dL
    Free testosterone 98 pg/mL
    Estradiol 28 pg/mL
    SHBG 42.5 nmol/L
    Cortisol 14.2 ug/dL
    TSH 2.1 mIU/L
    Free T3 3.1 pg/mL
    Free T4 1.2 ng/dL
    ApoB 140 mg/dL H
    LDL cholesterol 131 mg/dL H
    HDL cholesterol 48 mg/dL
    hs-CRP 4.1 mg/L H
    Total cholesterol 212 mg/dL H
    Remnant cholesterol 33 mg/dL
    Triglycerides 165 mg/dL H
    Vitamin D 18 ng/mL L
    Ferritin 95 ng/mL
    Fructosamine 240 umol/L
    Albumin 4.3 g/dL
    Uric acid 6.9 mg/dL
    Hematocrit 44 %
    Hemoglobin 12.0 g/dL L
    Creatinine 1.0 mg/dL
    eGFR 95 mL/min/1.73m2
    ALP 70 U/L
    GGT 44 U/L
    """;

  private static async Task<(HttpClient Ada, Guid AdaPerson, HttpClient Bob, Guid BobPerson)> HouseholdAsync(ApiFactory factory)
  {
    var suffix = Guid.NewGuid().ToString("N")[..8];
    var ada = factory.CreateClient();
    var adaAuth = await RegisterAsync(ada, $"ada-{suffix}@example.com", "Ada");
    var invite = await (await ada.PostJsonAsync("/api/household/invites", new { })).ReadJsonAsync();
    var bob = factory.CreateClient();
    var bobAuth = await RegisterAsync(bob, $"bob-{suffix}@example.com", "Bob", invite.GetProperty("code").GetString());
    return (ada, PersonId(adaAuth), bob, PersonId(bobAuth));
  }

  private static async Task<JsonElement> RegisterAsync(HttpClient client, string email, string name, string? inviteCode = null)
  {
    var response = await client.PostJsonAsync("/api/auth/register", new { email, password = "correct horse battery staple", displayName = name, inviteCode });
    response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    var auth = await response.ReadJsonAsync();
    client.UseBearer(auth.GetProperty("accessToken").GetString()!);
    return auth;
  }

  private static Guid PersonId(JsonElement auth) => auth.GetProperty("user").GetProperty("person").GetProperty("id").GetGuid();

  private static async Task SetMaleAsync(HttpClient client, string name)
  {
    var response = await client.PutJsonAsync("/api/me/profile", new { displayName = name, sexAtBirth = "male", units = "metric", timeZone = "Europe/London" });
    response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
  }

  /// <summary>Parses the block and turns the candidates into the create request's results.</summary>
  private static async Task<List<object>> CandidatesAsResultsAsync(HttpClient client)
  {
    var parsed = await client.PostJsonAsync("/api/labs/parse", new { text = RythmBlock });
    parsed.StatusCode.Should().Be(HttpStatusCode.OK, await parsed.Content.ReadAsStringAsync());
    var body = await parsed.ReadJsonAsync();
    return body.GetProperty("candidates").EnumerateArray()
      .Select(c => (object)new
      {
        metricKey = c.GetProperty("metricKey").GetString(),
        value = c.GetProperty("value").GetDouble(),
        unit = c.GetProperty("unit").GetString(),
        flag = c.GetProperty("flag").ValueKind == JsonValueKind.Null ? null : c.GetProperty("flag").GetString(),
        rawText = c.GetProperty("rawText").GetString(),
      })
      .ToList();
  }

  [Fact]
  public async Task Endpoints_WithoutABearer_Are401()
  {
    await using var factory = new ApiFactory();
    var anon = factory.CreateClient();

    (await anon.PostJsonAsync("/api/labs/parse", new { text = "ApoB 92 mg/dL" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await anon.PostJsonAsync("/api/labs/reports", new { })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await anon.GetAsync("/api/labs/reports")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await anon.GetAsync($"/api/labs/reports/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await anon.DeleteAsync($"/api/labs/reports/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await anon.GetAsync("/api/labs/markers")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await anon.GetAsync("/api/labs/markers/lab.apob")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await anon.GetAsync("/api/labs/summary.pdf")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }

  [Fact]
  public async Task Version_ListsTheLabsFeatures()
  {
    await using var factory = new ApiFactory();
    var features = (await (await factory.CreateClient().GetAsync("/api/system/version")).ReadJsonAsync()).GetProperty("features").EnumerateArray().Select(f => f.GetString()).ToList();

    features.Should().Contain("labs.reports").And.Contain("labs.pdf");
  }

  [Fact]
  public async Task Parse_TextBlock_ReturnsCandidates_AndMultipartIs501()
  {
    await using var factory = new ApiFactory();
    var (ada, _, _, _) = await HouseholdAsync(factory);

    var parsed = await ada.PostJsonAsync("/api/labs/parse", new { text = RythmBlock });
    parsed.StatusCode.Should().Be(HttpStatusCode.OK, await parsed.Content.ReadAsStringAsync());
    var body = await parsed.ReadJsonAsync();
    var candidates = body.GetProperty("candidates").EnumerateArray().ToList();
    candidates.Should().HaveCount(26);
    var apob = candidates.Single(c => c.GetProperty("metricKey").GetString() == "lab.apob");
    apob.GetProperty("value").GetDouble().Should().Be(140);
    apob.GetProperty("unit").GetString().Should().Be("mg/dL");
    apob.GetProperty("flag").GetString().Should().Be("high");
    apob.GetProperty("confidence").GetDouble().Should().Be(1.0);
    apob.GetProperty("displayName").GetString().Should().Be("ApoB");
    body.GetProperty("unmatchedLines").EnumerateArray().Select(l => l.GetString()).Should().Equal("Rythm Health", "Monthly panel");

    using var form = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "image", "panel.jpg" } };
    var multipart = await ada.PostAsync("/api/labs/parse", form);
    multipart.StatusCode.Should().Be(HttpStatusCode.NotImplemented);
    multipart.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    (await multipart.ReadJsonAsync()).GetProperty("detail").GetString().Should().Contain("server OCR not available in this version");

    (await ada.PostJsonAsync("/api/labs/parse", new { text = new string('x', 20001) })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await (await ada.PostJsonAsync("/api/labs/parse", new { text = "" })).ReadJsonAsync()).GetProperty("candidates").GetArrayLength().Should().Be(0);
  }

  [Fact]
  public async Task Create_List_Get_Markers_Today_Pdf_Share_Delete_EndToEnd()
  {
    await using var factory = new ApiFactory();
    var (ada, adaPerson, bob, bobPerson) = await HouseholdAsync(factory);
    await SetMaleAsync(ada, "Ada");
    var results = await CandidatesAsResultsAsync(ada);
    var collectedOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2);

    // Create: units converted, three ratios derived, image stored encrypted.
    var image = Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4 });
    var created = await ada.PostJsonAsync("/api/labs/reports", new { provider = "rythm", collectedOn, results, image, notes = "Fasted, 08:10 draw." });
    created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
    var report = await created.ReadJsonAsync();
    var reportId = report.GetProperty("id").GetGuid();
    report.GetProperty("personId").GetGuid().Should().Be(adaPerson);
    report.GetProperty("provider").GetString().Should().Be("rythm");
    report.GetProperty("hasImage").GetBoolean().Should().BeTrue();
    report.GetProperty("notes").GetString().Should().Be("Fasted, 08:10 draw.");
    report.GetProperty("confirmedAt").ValueKind.Should().Be(JsonValueKind.String);
    var stored = report.GetProperty("results").EnumerateArray().ToList();
    stored.Should().HaveCount(29, "26 read plus 3 derived ratios");
    var derived = stored.Where(r => r.GetProperty("derived").GetBoolean()).ToList();
    derived.Select(r => r.GetProperty("metricKey").GetString()).Should().BeEquivalentTo(new[] { "lab.ldl_apob_ratio", "lab.tc_hdl_ratio", "lab.tg_hdl_ratio" });
    derived.Single(r => r.GetProperty("metricKey").GetString() == "lab.ldl_apob_ratio").GetProperty("value").GetDouble().Should().Be(0.94);
    derived.Single(r => r.GetProperty("metricKey").GetString() == "lab.tc_hdl_ratio").GetProperty("value").GetDouble().Should().Be(4.42);
    derived.Single(r => r.GetProperty("metricKey").GetString() == "lab.tg_hdl_ratio").GetProperty("value").GetDouble().Should().Be(3.44);
    stored.Should().OnlyContain(r => r.GetProperty("unit").GetString() != "mmol/L");
    stored.Single(r => r.GetProperty("metricKey").GetString() == "lab.apob").GetProperty("flag").GetString().Should().Be("high");
    stored[0].GetProperty("metricKey").GetString().Should().Be("lab.total_testosterone", "panel order");

    // Validation: unknown marker, non-lab metric, duplicate, future date, bad flag, bad unit.
    (await ada.PostJsonAsync("/api/labs/reports", new { provider = "rythm", collectedOn, results = new[] { new { metricKey = "steps", value = 1.0 } } })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await ada.PostJsonAsync("/api/labs/reports", new { provider = "rythm", collectedOn, results = new[] { new { metricKey = "lab.apob", value = 90.0 }, new { metricKey = "lab.apob", value = 91.0 } } })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await ada.PostJsonAsync("/api/labs/reports", new { provider = "rythm", collectedOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3), results = new[] { new { metricKey = "lab.apob", value = 90.0 } } })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await ada.PostJsonAsync("/api/labs/reports", new { provider = "rythm", collectedOn, results = new[] { new { metricKey = "lab.apob", value = 90.0, flag = "meh" } } })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await ada.PostJsonAsync("/api/labs/reports", new { provider = "rythm", collectedOn, results = new[] { new { metricKey = "lab.apob", value = 90.0, unit = "nmol/L" } } })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await ada.PostJsonAsync("/api/labs/reports", new { provider = "quest", collectedOn, results = new[] { new { metricKey = "lab.apob", value = 90.0 } } })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    // A second, older panel in mmol/L so markers have a previous value and the trend rule has two panels.
    var older = await ada.PostJsonAsync("/api/labs/reports", new
    {
      provider = "other",
      collectedOn = collectedOn.AddDays(-30),
      results = new object[]
      {
        new { metricKey = "lab.apob", value = 118.0, unit = "mg/dL" },
        new { metricKey = "lab.ldl", value = 2.6, unit = "mmol/L" },
        new { metricKey = "lab.hdl", value = 1.3, unit = "mmol/L" },
        new { metricKey = "lab.total_cholesterol", value = 4.9, unit = "mmol/L" },
        new { metricKey = "lab.triglycerides", value = 1.2, unit = "mmol/L", flag = "N" },
      },
    });
    older.StatusCode.Should().Be(HttpStatusCode.OK, await older.Content.ReadAsStringAsync());
    var olderBody = await older.ReadJsonAsync();
    var olderId = olderBody.GetProperty("id").GetGuid();
    olderBody.GetProperty("results").EnumerateArray().Single(r => r.GetProperty("metricKey").GetString() == "lab.ldl").GetProperty("value").GetDouble().Should().BeApproximately(100.54, 0.01, "2.6 mmol/L times 38.67");
    olderBody.GetProperty("results").EnumerateArray().Single(r => r.GetProperty("metricKey").GetString() == "lab.triglycerides").GetProperty("flag").GetString().Should().Be("normal");

    // List and get.
    var list = await (await ada.GetAsync("/api/labs/reports")).ReadJsonAsync();
    list.GetArrayLength().Should().Be(2);
    list[0].GetProperty("id").GetGuid().Should().Be(reportId, "newest collected_on first");
    var one = await ada.GetAsync($"/api/labs/reports/{reportId}");
    one.StatusCode.Should().Be(HttpStatusCode.OK);
    (await one.ReadJsonAsync()).GetProperty("results").GetArrayLength().Should().Be(29);
    (await ada.GetAsync($"/api/labs/reports/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

    // Samples: one lab.* sample per result at noon local on collected_on.
    var samples = await (await ada.GetAsync("/api/data/samples?metric=lab.apob")).ReadJsonAsync();
    samples.GetProperty("items").GetArrayLength().Should().Be(2);
    samples.GetProperty("items")[0].GetProperty("value").GetDouble().Should().Be(140);
    var start = samples.GetProperty("items")[0].GetProperty("start").GetDateTime().ToUniversalTime();
    DateOnly.FromDateTime(start).Should().Be(collectedOn);
    start.Hour.Should().BeInRange(11, 12, "noon in Europe/London is 11:00 or 12:00 UTC");
    (await (await ada.GetAsync("/api/data/samples?metric=lab.ldl_apob_ratio")).ReadJsonAsync()).GetProperty("items").GetArrayLength().Should().Be(2, "derived ratios are samples too");
    (await (await ada.GetAsync("/api/sources")).ReadJsonAsync()).EnumerateArray().Should().Contain(s => s.GetProperty("kind").GetString() == "lab_scan" && s.GetProperty("deviceName").GetString() == "Lab results");

    // Markers: panel order, latest and previous, population range for a male, tier, last three.
    var markers = await (await ada.GetAsync("/api/labs/markers")).ReadJsonAsync();
    markers.GetArrayLength().Should().Be(30);
    markers[0].GetProperty("metricKey").GetString().Should().Be("lab.total_testosterone");
    var byKey = markers.EnumerateArray().ToDictionary(m => m.GetProperty("metricKey").GetString()!);
    var apob = byKey["lab.apob"];
    apob.GetProperty("latestValue").GetDouble().Should().Be(140);
    apob.GetProperty("latestDate").GetString().Should().Be(collectedOn.ToString("yyyy-MM-dd"));
    apob.GetProperty("latestFlag").GetString().Should().Be("high");
    apob.GetProperty("previousValue").GetDouble().Should().Be(118);
    apob.GetProperty("populationRange").GetProperty("low").GetDouble().Should().Be(66);
    apob.GetProperty("populationRange").GetProperty("high").GetDouble().Should().Be(133);
    apob.GetProperty("optimalRange").GetProperty("high").GetDouble().Should().Be(90);
    apob.GetProperty("tier").GetString().Should().Be("watch");
    apob.GetProperty("position").GetString().Should().Be("above");
    apob.GetProperty("group").GetString().Should().Be("heart");
    apob.GetProperty("goodDirection").GetString().Should().Be("down");
    apob.GetProperty("lastThree").GetArrayLength().Should().Be(2);
    apob.GetProperty("applicable").GetBoolean().Should().BeTrue();
    byKey["lab.progesterone"].GetProperty("applicable").GetBoolean().Should().BeFalse("female-only marker, male person");
    byKey["lab.progesterone"].GetProperty("populationRange").ValueKind.Should().Be(JsonValueKind.Null);
    byKey["lab.ldl_apob_ratio"].GetProperty("tier").ValueKind.Should().Be(JsonValueKind.Null);
    byKey["lab.ldl_apob_ratio"].GetProperty("latestValue").GetDouble().Should().Be(0.94);
    byKey["lab.hemoglobin"].GetProperty("position").GetString().Should().Be("below");
    apob.GetProperty("severity").GetString().Should().Be("watch");
    byKey["lab.hemoglobin"].GetProperty("severity").GetString().Should().Be("clinician", "low hemoglobin is a clinician trigger, whatever the row's base tier");
    foreach (var m in markers.EnumerateArray())
    {
      var inside = m.GetProperty("position").ValueKind == JsonValueKind.Null || m.GetProperty("position").GetString() == "inside";
      if (inside) m.GetProperty("severity").ValueKind.Should().Be(JsonValueKind.Null, $"{m.GetProperty("metricKey")} is inside its range or has none");
    }

    var detail = await ada.GetAsync("/api/labs/markers/lab.apob");
    detail.StatusCode.Should().Be(HttpStatusCode.OK, await detail.Content.ReadAsStringAsync());
    var detailBody = await detail.ReadJsonAsync();
    detailBody.GetProperty("marker").GetProperty("metricKey").GetString().Should().Be("lab.apob");
    detailBody.GetProperty("blurb").GetString().Should().NotBeNullOrEmpty();
    detailBody.GetProperty("whatMovesIt").GetString().Should().Contain("Statins");
    detailBody.GetProperty("sourceUrl").GetString().Should().StartWith("https://");
    detailBody.GetProperty("history").GetArrayLength().Should().Be(2);
    (await ada.GetAsync("/api/labs/markers/apob")).StatusCode.Should().Be(HttpStatusCode.OK, "the prefix is optional");
    (await ada.GetAsync("/api/labs/markers/steps")).StatusCode.Should().Be(HttpStatusCode.NotFound);

    // Today and the archive: the lab rules ran for today when the report was confirmed.
    var archive = await (await ada.GetAsync("/api/insights")).ReadJsonAsync();
    var kinds = archive.EnumerateArray().Select(i => i.GetProperty("kind").GetString()).ToList();
    kinds.Should().Contain("lab_out_of_range");
    var labCard = archive.EnumerateArray().Single(i => i.GetProperty("kind").GetString() == "lab_out_of_range");
    labCard.GetProperty("severity").GetString().Should().Be("clinician", "hemoglobin 12.0 is below the male range");
    labCard.GetProperty("text").GetString().Should().Contain("Hemoglobin").And.Contain(collectedOn.ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture));
    labCard.GetProperty("metrics").EnumerateArray().Select(m => m.GetString()).Should().Contain("lab.apob");
    kinds.Should().Contain("lab_trend", "ApoB went from 118 to 140, up 18 percent");
    var today = await (await ada.GetAsync("/api/today")).ReadJsonAsync();
    today.GetProperty("cards").EnumerateArray().Select(c => c.GetProperty("kind").GetString()).Should().Contain("lab_out_of_range");

    // PDF.
    var pdf = await ada.GetAsync("/api/labs/summary.pdf");
    pdf.StatusCode.Should().Be(HttpStatusCode.OK, await pdf.Content.ReadAsStringAsync());
    pdf.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
    var bytes = await pdf.Content.ReadAsByteArrayAsync();
    bytes.Length.Should().BeGreaterThan(1000);
    System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    pdf.Content.Headers.ContentDisposition!.FileName.Should().Contain("labs-summary-");
    (await ada.GetAsync("/api/labs/summary.pdf?from=2026-10-20&to=2026-10-01")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    // Bob: nothing until labs are shared, then reads but no deletes.
    (await bob.GetAsync($"/api/labs/reports?personId={adaPerson}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await bob.GetAsync($"/api/labs/reports/{reportId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await bob.GetAsync($"/api/labs/markers?personId={adaPerson}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await bob.GetAsync($"/api/labs/markers/lab.apob?personId={adaPerson}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await bob.GetAsync($"/api/labs/summary.pdf?personId={adaPerson}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await bob.GetAsync($"/api/labs/markers?personId={Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await ada.PutJsonAsync($"/api/household/shares/{bobPerson}", new { categories = new[] { "activity" } })).StatusCode.Should().Be(HttpStatusCode.OK);
    (await bob.GetAsync($"/api/labs/reports?personId={adaPerson}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "activity is not labs");
    (await ada.PutJsonAsync($"/api/household/shares/{bobPerson}", new { categories = new[] { "labs" } })).StatusCode.Should().Be(HttpStatusCode.OK);
    (await (await bob.GetAsync($"/api/labs/reports?personId={adaPerson}")).ReadJsonAsync()).GetArrayLength().Should().Be(2);
    (await bob.GetAsync($"/api/labs/reports/{reportId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    (await (await bob.GetAsync($"/api/labs/markers?personId={adaPerson}")).ReadJsonAsync()).GetArrayLength().Should().Be(30);
    var bobPdf = await bob.GetAsync($"/api/labs/summary.pdf?personId={adaPerson}");
    bobPdf.StatusCode.Should().Be(HttpStatusCode.OK);
    bobPdf.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
    (await bob.DeleteAsync($"/api/labs/reports/{reportId}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "only the owner deletes");

    // Delete: report, results and samples go.
    (await ada.DeleteAsync($"/api/labs/reports/{reportId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await ada.DeleteAsync($"/api/labs/reports/{reportId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await (await ada.GetAsync("/api/labs/reports")).ReadJsonAsync()).GetArrayLength().Should().Be(1);
    (await ada.GetAsync($"/api/labs/reports/{reportId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await (await ada.GetAsync("/api/data/samples?metric=lab.apob")).ReadJsonAsync()).GetProperty("items").GetArrayLength().Should().Be(1, "the deleted report's sample is gone, the older one stays");
    (await (await ada.GetAsync("/api/labs/markers")).ReadJsonAsync()).EnumerateArray().Single(m => m.GetProperty("metricKey").GetString() == "lab.apob").GetProperty("latestValue").GetDouble().Should().Be(118);
    (await ada.DeleteAsync($"/api/labs/reports/{olderId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await (await ada.GetAsync("/api/data/samples?metric=lab.apob")).ReadJsonAsync()).GetProperty("items").GetArrayLength().Should().Be(0);

    // An empty PDF still renders.
    var emptyPdf = await ada.GetAsync("/api/labs/summary.pdf");
    emptyPdf.StatusCode.Should().Be(HttpStatusCode.OK);
    (await emptyPdf.Content.ReadAsByteArrayAsync()).Length.Should().BeGreaterThan(0);
  }

  [Fact]
  public async Task Pdf_IsRateLimitedToTenAMinute()
  {
    await using var factory = new ApiFactory();
    var (ada, _, _, _) = await HouseholdAsync(factory);

    var statuses = new List<HttpStatusCode>();
    for (var i = 0; i < 11; i++) statuses.Add((await ada.GetAsync("/api/labs/summary.pdf")).StatusCode);

    statuses.Take(10).Should().OnlyContain(s => s == HttpStatusCode.OK);
    statuses[10].Should().Be(HttpStatusCode.TooManyRequests);
  }

  [Fact]
  public async Task Markers_WithNoReports_ListsEveryMarkerEmpty()
  {
    await using var factory = new ApiFactory();
    var (ada, _, _, _) = await HouseholdAsync(factory);

    var markers = await (await ada.GetAsync("/api/labs/markers")).ReadJsonAsync();

    markers.GetArrayLength().Should().Be(30);
    markers.EnumerateArray().Should().OnlyContain(m => m.GetProperty("latestValue").ValueKind == JsonValueKind.Null && m.GetProperty("lastThree").GetArrayLength() == 0);
    var tsh = markers.EnumerateArray().Single(m => m.GetProperty("metricKey").GetString() == "lab.tsh");
    tsh.GetProperty("populationRange").GetProperty("low").GetDouble().Should().Be(0.3, "a both row applies whatever the sex");
    var apobRange = markers.EnumerateArray().Single(m => m.GetProperty("metricKey").GetString() == "lab.apob").GetProperty("populationRange");
    apobRange.GetProperty("high").GetDouble().Should().Be(133, "sex is unspecified, so ApoB shows the combined range: the higher of the two highs");
  }
}
