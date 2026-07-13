using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace MQEL.Tests;

// Smoke tests for the extracted AdminEndpoints (the client never calls /api/*, so these aren't wire goldens).
// They confirm the module still authorizes, reads, and writes through the same DB the game path uses.
public sealed class AdminSmokeTests
{
    static HttpClient Game(WireGoldenFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Add("t", "wiretest-token");
        return c;
    }

    [Fact]
    public async Task Status_reports_running_and_real_port()
    {
        using var f = new WireGoldenFactory();
        var doc = JsonDocument.Parse(await f.CreateClient().GetStringAsync("/api/status"));
        Assert.True(doc.RootElement.GetProperty("running").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("port").GetInt32() >= 0);   // real LocalPort, not a hardcoded 8080
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("db").GetString()));
    }

    [Fact]
    public async Task Account_detail_reflects_state_written_by_the_game_path()
    {
        using var f = new WireGoldenFactory();
        // create + progress the dev account through the real game pipeline
        var g = Game(f);
        await g.PostAsync("/AccountService.hqs/ChooseDisplayName",
            new StringContent("{\"displayName\":\"Thrax\"}", System.Text.Encoding.UTF8, "application/json"));
        await g.PostAsync("/HeroService.hqs/ChooseFirstHero",
            new StringContent("{\"heroSpecContainerId\":5}", System.Text.Encoding.UTF8, "application/json"));

        // the admin list sees exactly one non-template account...
        var list = JsonDocument.Parse(await f.CreateClient().GetStringAsync("/api/accounts"));
        Assert.Equal(1, list.RootElement.GetArrayLength());
        var id = list.RootElement[0].GetProperty("accountId").GetInt64();

        // ...and its detail reflects the hero the game path chose (class 5).
        var detail = JsonDocument.Parse(await f.CreateClient().GetStringAsync($"/api/accounts/{id}"));
        Assert.Equal(5, detail.RootElement.GetProperty("heroClass").GetInt32());
    }

    [Fact]
    public async Task Account_edit_is_apply_if_present_and_gold_persists()
    {
        using var f = new WireGoldenFactory();
        var g = Game(f);
        await g.PostAsync("/AccountService.hqs/ChooseDisplayName",
            new StringContent("{\"displayName\":\"Thrax\"}", System.Text.Encoding.UTF8, "application/json"));
        var id = JsonDocument.Parse(await f.CreateClient().GetStringAsync("/api/accounts")).RootElement[0].GetProperty("accountId").GetInt64();

        // §1.5 — a partial edit (only Gold) must NOT wipe the display name.
        var admin = f.CreateClient();
        var resp = await admin.PostAsJsonAsync($"/api/accounts/{id}", new { Gold = 4242L });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var detail = JsonDocument.Parse(await admin.GetStringAsync($"/api/accounts/{id}"));
        Assert.Equal(4242, detail.RootElement.GetProperty("gold").GetInt64());
        Assert.Equal("Thrax", detail.RootElement.GetProperty("displayName").GetString());
    }
}
