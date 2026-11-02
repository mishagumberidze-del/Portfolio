using System.IO.Compression;
using SpotifyTaste.Infrastructure.Import;

namespace SpotifyTaste.Tests;

public sealed class ExportReaderTests
{
    [Fact]
    public async Task Directory_discovery_classifies_actual_spotify_file_patterns()
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "Streaming_History_Audio_2025.json"), "[]");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "YourLibrary.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "Streaming_History_Video_2025.json"), "[]");

        var entries = await new SpotifyExportReader().DiscoverAsync(temp.Path);
        Assert.Contains(entries, x => x.Kind == SpotifyExportEntryKind.ExtendedAudioHistory);
        Assert.Contains(entries, x => x.Kind == SpotifyExportEntryKind.Library);
        Assert.Contains(entries, x => x.Kind == SpotifyExportEntryKind.OtherJson);
    }

    [Fact]
    public async Task Zip_entries_can_be_opened_after_discovery_archive_is_closed()
    {
        using var temp = new TempDirectory();
        var zipPath = Path.Combine(temp.Path, "spotify.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("Spotify Extended Streaming History/Streaming_History_Audio_2026.json");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("[{\"ts\":\"2026-01-01T00:00:00Z\"}]");
        }

        var discovered = await new SpotifyExportReader().DiscoverAsync(zipPath);
        await using var stream = await Assert.Single(discovered).OpenStreamAsync(default);
        using var reader = new StreamReader(stream);
        Assert.Contains("2026-01-01", await reader.ReadToEndAsync());
    }
}

internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"spotify-taste-tests-{Guid.NewGuid():N}");
    public TempDirectory() => Directory.CreateDirectory(Path);
    public void Dispose() => Directory.Delete(Path, true);
}
