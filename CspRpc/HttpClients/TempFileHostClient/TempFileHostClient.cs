using System.IO;
using System.Net.Http;

using Microsoft.AspNetCore.Http.Extensions;

namespace CspRpc.HttpClients.TempFileHostClient;

internal sealed class TempFileHostClient : ITempFileHostClient
{
    private static readonly Lazy<TempFileHostClient> _lazyInstance = new Lazy<TempFileHostClient>(() => new TempFileHostClient());
    /// <summary>
    /// Retrieves the singleton instance of <see cref="TempFileHostClient"/>
    /// </summary>
    public static TempFileHostClient Instance => _lazyInstance.Value;

    private static readonly HttpClient _client = new HttpClient()
    {
        BaseAddress = new Uri("https://uguu.se/"),
        DefaultRequestHeaders =
        {
            { "User-Agent", "CspRpc" }
        }
    };

    private TempFileHostClient() { }

    public async Task<string> Upload(Stream data)
    {
        var query = new QueryBuilder()
        {
            { "output", "text" }
        };
        using (var mfd = new MultipartFormDataContent()
        {
            { new StreamContent(data), "files[]", $"{new Guid()}.png" }
        })
        using (var response = await _client.PostAsync($"upload{query.ToQueryString()}", mfd))
        {
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadAsStringAsync()).Trim();
        }
    }
}
