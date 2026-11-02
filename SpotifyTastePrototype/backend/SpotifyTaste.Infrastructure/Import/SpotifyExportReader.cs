using System.IO.Compression;

namespace SpotifyTaste.Infrastructure.Import;

public enum SpotifyExportEntryKind { ExtendedAudioHistory, Library, OtherJson }

public sealed record SpotifyExportEntry(string FileName, SpotifyExportEntryKind Kind, Func<CancellationToken, Task<Stream>> OpenStreamAsync);

public sealed class SpotifyExportReader
{
    public Task<IReadOnlyList<SpotifyExportEntry>> DiscoverAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(sourcePath)) return Task.FromResult<IReadOnlyList<SpotifyExportEntry>>(DiscoverDirectory(sourcePath));
        if (File.Exists(sourcePath) && Path.GetExtension(sourcePath).Equals(".zip", StringComparison.OrdinalIgnoreCase)) return Task.FromResult<IReadOnlyList<SpotifyExportEntry>>(DiscoverZip(sourcePath));
        throw new FileNotFoundException("Spotify export directory or ZIP file was not found.", sourcePath);
    }

    private static List<SpotifyExportEntry> DiscoverDirectory(string sourcePath) => Directory
        .EnumerateFiles(sourcePath, "*.json", SearchOption.AllDirectories)
        .Select(file => new SpotifyExportEntry(
            Path.GetRelativePath(sourcePath, file).Replace('\\', '/'),
            Classify(file),
            _ => Task.FromResult<Stream>(File.OpenRead(file))))
        .OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static List<SpotifyExportEntry> DiscoverZip(string sourcePath)
    {
        using var archive = ZipFile.OpenRead(sourcePath);
        return archive.Entries
            .Where(x => x.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Select(entry =>
            {
                var entryName = entry.FullName;
                return new SpotifyExportEntry(entryName, Classify(entryName), async cancellationToken =>
                {
                    using var reopenedArchive = ZipFile.OpenRead(sourcePath);
                    var reopenedEntry = reopenedArchive.GetEntry(entryName) ?? throw new InvalidDataException($"ZIP entry disappeared: {entryName}");
                    var memory = new MemoryStream((int)Math.Min(reopenedEntry.Length, int.MaxValue));
                    await using var entryStream = reopenedEntry.Open();
                    await entryStream.CopyToAsync(memory, cancellationToken);
                    memory.Position = 0;
                    return memory;
                });
            })
            .OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static SpotifyExportEntryKind Classify(string name)
    {
        var fileName = Path.GetFileName(name);
        if (fileName.StartsWith("Streaming_History_Audio_", StringComparison.OrdinalIgnoreCase)) return SpotifyExportEntryKind.ExtendedAudioHistory;
        if (fileName.Contains("YourLibrary", StringComparison.OrdinalIgnoreCase) || fileName.Contains("Library", StringComparison.OrdinalIgnoreCase)) return SpotifyExportEntryKind.Library;
        return SpotifyExportEntryKind.OtherJson;
    }
}

