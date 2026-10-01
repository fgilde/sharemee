using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Shareme.Api.Tests;

public sealed class ApiTests(ApiFixture api)
{
    [Fact]
    public async Task System_info_returns_product()
    {
        using var client = api.Factory.CreateClient();

        var info = await client.GetFromJsonAsync<JsonElement>("/api/v1/system/info", TestContext.Current.CancellationToken);

        info.GetProperty("product").GetString().ShouldBe("Shareme");
        info.GetProperty("version").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Health_includes_database()
    {
        using var client = api.Factory.CreateClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OpenApi_documents_system_info()
    {
        using var client = api.Factory.CreateClient();

        var document = await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        document.ShouldContain("/api/v1/system/info");
    }
}
