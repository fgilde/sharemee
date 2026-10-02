using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace Shareme.Api.Tests;

/// <summary>Answers like OpenAI, Anthropic and Azure AI Vision, by host, and keeps the requests for assertions.</summary>
public sealed class FakeAi : HttpMessageHandler
{
    public static readonly FakeAi Instance = new();

    private const string Answer = """{"tags":[{"name":"Beach","confidence":0.92},{"name":"sky","confidence":0.3},{"name":"Sand","confidence":0.8}],"description":"A sunny beach.","descriptionConfidence":0.9}""";

    public ConcurrentQueue<(HttpRequestMessage Request, string Body)> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Enqueue((request, body));
        var json = request.RequestUri!.Host switch
        {
            "openai.test" => System.Text.Json.JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content = Answer } } } }),
            "anthropic.test" => System.Text.Json.JsonSerializer.Serialize(new { content = new[] { new { type = "text", text = "```json" + Environment.NewLine + Answer + Environment.NewLine + "```" } } }),
            "vision.test" => """{"tagsResult":{"values":[{"name":"beach","confidence":0.95},{"name":"water","confidence":0.4}]},"captionResult":{"text":"a beach with sand","confidence":0.85}}""",
            _ => null,
        };
        return json is null
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
