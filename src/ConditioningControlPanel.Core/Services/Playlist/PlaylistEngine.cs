using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConditioningControlPanel.Core.Services.Playlist
{
    /// <summary>One entry in a playlist. Path is host-resolvable (local file or URL).</summary>
    public sealed class PlaylistTrack
    {
        [JsonPropertyName("path")]  public string Path  { get; set; } = "";
        [JsonPropertyName("title")] public string Title { get; set; } = "";

        /// <summary>Optional haptics pattern (local file or URL) synced to this track.</summary>
        [JsonPropertyName("haptics"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? HapticsPath { get; set; }

        [JsonPropertyName("durationMs"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public long? DurationMs { get; set; }

        /// <summary>Free-form type tag, e.g. induction/deepener/loop/music.</summary>
        [JsonPropertyName("kind"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Kind { get; set; }
    }

    /// <summary>A named, ordered set of tracks. Serializes to plain JSON on disk.</summary>
    public sealed class PlaylistDefinition
    {
        [JsonPropertyName("name")]   public string Name { get; set; } = "";
        [JsonPropertyName("tracks")] public List<PlaylistTrack> Tracks { get; set; } = new();

        public static PlaylistDefinition LoadFile(string path) =>
            JsonSerializer.Deserialize<PlaylistDefinition>(File.ReadAllText(path))
                ?? throw new InvalidDataException($"not a playlist: {path}");

        public void SaveFile(string path) =>
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public enum RepeatMode { Off, All, One }

    /// <summary>
    /// Native playlist engine (upstream's "playlists" were BambiCloud links inside the
    /// Windows-only WebView browser — that subsystem doesn't port, so this replaces it).
    /// Core owns selection state — current track, next/prev, shuffle, repeat — and raises
    /// TrackChanged; the host (Shell/LibVLC, or LV as DJ over the wire) does actual audio.
    /// </summary>
    public sealed class PlaylistEngine
    {
        private readonly Random _rng;
        private List<int> _order = new();   // play order as indexes into Current.Tracks
        private int _pos = -1;              // position within _order

        public PlaylistDefinition? Current { get; private set; }
        public bool Shuffle { get; private set; }
        public RepeatMode Repeat { get; set; } = RepeatMode.All;

        public event EventHandler<PlaylistTrack>? TrackChanged;
        public event EventHandler? PlaylistEnded;

        public PlaylistEngine(int? seed = null) => _rng = seed is { } s ? new Random(s) : new Random();

        public PlaylistTrack? CurrentTrack =>
            Current != null && _pos >= 0 && _pos < _order.Count ? Current.Tracks[_order[_pos]] : null;

        public void Load(PlaylistDefinition playlist, bool autoStart = true)
        {
            Current = playlist;
            RebuildOrder();
            _pos = -1;
            if (autoStart) Next();
        }

        public void SetShuffle(bool on)
        {
            if (Shuffle == on) return;
            Shuffle = on;
            var current = CurrentTrack;
            RebuildOrder();
            // keep the playing track as the anchor at the current position
            if (current != null && Current != null)
            {
                var idx = Current.Tracks.IndexOf(current);
                var orderPos = _order.IndexOf(idx);
                if (orderPos >= 0) (_order[orderPos], _order[Math.Max(_pos, 0)]) = (_order[Math.Max(_pos, 0)], _order[orderPos]);
            }
        }

        /// <summary>Advance. Returns the new track, or null when the playlist ends (Repeat=Off).</summary>
        public PlaylistTrack? Next()
        {
            if (Current == null || Current.Tracks.Count == 0) return null;

            if (Repeat == RepeatMode.One && _pos >= 0)
            {
                TrackChanged?.Invoke(this, CurrentTrack!);
                return CurrentTrack;
            }

            if (_pos + 1 >= _order.Count)
            {
                if (Repeat != RepeatMode.All)
                {
                    PlaylistEnded?.Invoke(this, EventArgs.Empty);
                    return null;
                }
                if (Shuffle) RebuildOrder(); // reshuffle each full pass
                _pos = -1;
            }

            _pos++;
            TrackChanged?.Invoke(this, CurrentTrack!);
            return CurrentTrack;
        }

        public PlaylistTrack? Prev()
        {
            if (Current == null || Current.Tracks.Count == 0) return null;
            _pos = _pos <= 0 ? (Repeat == RepeatMode.All ? _order.Count - 1 : 0) : _pos - 1;
            TrackChanged?.Invoke(this, CurrentTrack!);
            return CurrentTrack;
        }

        /// <summary>Jump to a track by (case-insensitive) title or path substring. Returns it, or null.</summary>
        public PlaylistTrack? JumpTo(string query)
        {
            if (Current == null) return null;
            var idx = Current.Tracks.FindIndex(t =>
                t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.Path.Contains(query, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return null;
            var orderPos = _order.IndexOf(idx);
            if (orderPos < 0) return null;
            _pos = orderPos;
            TrackChanged?.Invoke(this, CurrentTrack!);
            return CurrentTrack;
        }

        private void RebuildOrder()
        {
            var n = Current?.Tracks.Count ?? 0;
            _order = Enumerable.Range(0, n).ToList();
            if (Shuffle)
                for (var i = n - 1; i > 0; i--)
                {
                    var j = _rng.Next(i + 1);
                    (_order[i], _order[j]) = (_order[j], _order[i]);
                }
        }
    }
}
