using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Shadeplay.Core;

/// <summary>Looks up a game's cover and banner on SteamGridDB with the user's own API key.</summary>
public sealed class SteamGridDb
{
    const string DefaultBaseUrl = "https://www.steamgriddb.com/api/v2";
    // JPEG and PNG only, and nothing marked as adult or flashing
    const string ImageFilter = "mimes=image/jpeg,image/png&nsfw=false&epilepsy=false";

    readonly HttpClient http;
    readonly string apiKey;
    readonly string baseUrl;

    public SteamGridDb(HttpClient http, string apiKey, string baseUrl = DefaultBaseUrl)
    {
        this.http = http;
        this.apiKey = apiKey;
        this.baseUrl = baseUrl.TrimEnd('/');
    }

    /// <returns>Image addresses, each null when there is none.</returns>
    /// <exception cref="UnauthorizedAccessException">The key was refused.</exception>
    public async Task<(string? Cover, string? Hero)> FindAsync(string name, string? steamAppId)
    {
        int? id = null;
        if (steamAppId != null)
            id = (await GetAsync($"/games/steam/{Uri.EscapeDataString(steamAppId)}")) is { ValueKind: JsonValueKind.Object } game
                ? IdOf(game) : null;
        if (id == null
            && await GetAsync($"/search/autocomplete/{Uri.EscapeDataString(name)}") is { ValueKind: JsonValueKind.Array } results
            && results.GetArrayLength() > 0)
            id = IdOf(results[0]);
        if (id == null) return (null, null);

        string? cover = FirstUrl(await GetAsync($"/grids/game/{id}?dimensions=600x900&{ImageFilter}"));
        string? hero = FirstUrl(await GetAsync($"/heroes/game/{id}?{ImageFilter}"));
        return (cover, hero);
    }

    static int? IdOf(JsonElement game) =>
        game.TryGetProperty("id", out var id) && id.TryGetInt32(out int value) ? value : null;

    static string? FirstUrl(JsonElement? images) =>
        images is { ValueKind: JsonValueKind.Array } list && list.GetArrayLength() > 0
        && list[0].TryGetProperty("url", out var url) ? url.GetString() : null;

    async Task<JsonElement?> GetAsync(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await http.SendAsync(request);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException();
        if (!response.IsSuccessStatusCode) return null; // 404: SteamGridDB doesn't know this game

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("data", out var data) ? data.Clone() : null;
    }
}
