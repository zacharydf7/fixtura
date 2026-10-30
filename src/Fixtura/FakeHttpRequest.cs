using System.Text.Json;

namespace Fixtura;

/// <summary>A request the app sent through a faked HTTP client.</summary>
public sealed class FakeHttpRequest
{
    internal FakeHttpRequest(HttpMethod method, Uri uri, IReadOnlyDictionary<string, string> headers, string? body)
    {
        Method = method;
        Uri = uri;
        Headers = headers;
        Body = body;
    }

    public HttpMethod Method { get; }

    public Uri Uri { get; }

    /// <summary>The request's and its content's headers, by case-insensitive name; a name's values are joined with ", ".</summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    public string? Body { get; }

    /// <summary>The body read as JSON, with the web defaults the app's own JSON helpers use.</summary>
    public T? Json<T>() => Body is null ? default : JsonSerializer.Deserialize<T>(Body, JsonSerializerOptions.Web);
}
