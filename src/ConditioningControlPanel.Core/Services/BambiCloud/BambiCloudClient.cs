using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Services.Playlist;

namespace ConditioningControlPanel.Core.Services.BambiCloud
{
    /// <summary>A playlist as returned by api.bambicloud.com/playlists.</summary>
    public sealed class BcPlaylist
    {
        [JsonPropertyName("id")]          public long Id { get; set; }
        [JsonPropertyName("name")]        public string Name { get; set; } = "";
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("expLevel")]    public string? ExpLevel { get; set; }
        [JsonPropertyName("uuid")]        public string? Uuid { get; set; }
        [JsonPropertyName("files")]       public List<BcFile> Files { get; set; } = new();
    }

    /// <summary>A file as returned inside playlists or from /files.</summary>
    public sealed class BcFile
    {
        [JsonPropertyName("id")]         public long Id { get; set; }
        [JsonPropertyName("name")]       public string Name { get; set; } = "";
        [JsonPropertyName("uuid")]       public string? Uuid { get; set; }
        [JsonPropertyName("duration")]   public long? DurationMs { get; set; }
        [JsonPropertyName("audioURL")]   public string? AudioUrl { get; set; }
        [JsonPropertyName("hapticsURL")] public string? HapticsUrl { get; set; }
        [JsonPropertyName("scriptURL")]  public string? ScriptUrl { get; set; }
        [JsonPropertyName("fileType")]   public string? FileType { get; set; }
        [JsonPropertyName("trackNum")]   public int? TrackNum { get; set; }
    }

    internal sealed class BcPlaylistsResponse
    {
        [JsonPropertyName("playlists")] public List<BcPlaylist> Playlists { get; set; } = new();
    }

    internal sealed class BcFilesResponse
    {
        [JsonPropertyName("files")] public List<BcFile> Files { get; set; } = new();
    }

    /// <summary>
    /// Native BambiCloud integration. Upstream's version was a paste-into-DevTools
    /// scraper that hardcoded links into BambiSprite.cs and opened them in the
    /// Windows WebView; the site is actually a React SPA over a clean JSON API,
    /// so this talks to the API directly and converts playlists into native
    /// <see cref="PlaylistDefinition"/>s the engine (and LV as DJ) already speaks.
    /// Files carry audio, haptics-pattern, and script URLs on the CDN.
    /// </summary>
    public sealed class BambiCloudClient : IDisposable
    {
        public const string DefaultApiBase = "https://api.bambicloud.com";

        private readonly HttpClient _http;
        private readonly bool _ownsHttp;

        public BambiCloudClient(HttpClient? http = null, string apiBase = DefaultApiBase)
        {
            _ownsHttp = http is null;
            _http = http ?? new HttpClient();
            if (_http.BaseAddress is null) _http.BaseAddress = new Uri(apiBase);
            if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
                _http.DefaultRequestHeaders.Add("User-Agent", "ccp-vesper-fork/1.0");
        }

        public async Task<List<BcPlaylist>> GetPlaylistsAsync(CancellationToken ct = default)
        {
            var json = await _http.GetStringAsync("/playlists", ct).ConfigureAwait(false);
            return ParsePlaylists(json);
        }

        public async Task<List<BcFile>> GetFilesAsync(CancellationToken ct = default)
        {
            var json = await _http.GetStringAsync("/files", ct).ConfigureAwait(false);
            return ParseFiles(json);
        }

        /// <summary>Find a playlist by numeric id, exact name, or name substring (case-insensitive).</summary>
        public async Task<BcPlaylist?> FindPlaylistAsync(string query, CancellationToken ct = default)
        {
            var all = await GetPlaylistsAsync(ct).ConfigureAwait(false);
            if (long.TryParse(query, out var id))
            {
                var byId = all.FirstOrDefault(p => p.Id == id);
                if (byId != null) return byId;
            }
            return all.FirstOrDefault(p => string.Equals(p.Name.Trim(), query.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? all.FirstOrDefault(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        // ── parsing/conversion (static + synchronous so tests need no network) ──

        public static List<BcPlaylist> ParsePlaylists(string json) =>
            JsonSerializer.Deserialize<BcPlaylistsResponse>(json)?.Playlists ?? new();

        public static List<BcFile> ParseFiles(string json) =>
            JsonSerializer.Deserialize<BcFilesResponse>(json)?.Files ?? new();

        /// <summary>
        /// Convert to a native playlist. Files without an audio URL are skipped;
        /// track order follows trackNum when present, else list order.
        /// </summary>
        public static PlaylistDefinition ToPlaylistDefinition(BcPlaylist bc)
        {
            var ordered = bc.Files
                .Where(f => !string.IsNullOrWhiteSpace(f.AudioUrl))
                .OrderBy(f => f.TrackNum ?? int.MaxValue)
                .ToList();

            return new PlaylistDefinition
            {
                Name = bc.Name.Trim(),
                Tracks = ordered.Select(f => new PlaylistTrack
                {
                    Path       = f.AudioUrl!,
                    Title      = f.Name,
                    HapticsPath = f.HapticsUrl,
                    DurationMs = f.DurationMs,
                    Kind       = f.FileType,
                }).ToList(),
            };
        }

        public void Dispose()
        {
            if (_ownsHttp) _http.Dispose();
        }
    }
}
