using Backend.Models;

namespace Backend.Import;

/// <summary>Routes an uploaded file to the importer registered for its extension.</summary>
public sealed class FileImportService
{
    private readonly IReadOnlyList<IFileImporter> _importers;

    public FileImportService(IEnumerable<IFileImporter> importers)
        => _importers = importers.ToList();

    public IReadOnlySet<string> SupportedExtensions =>
        _importers.SelectMany(i => i.Extensions).ToHashSet();

    /// <summary>Throws <see cref="NotSupportedException"/> for unknown extensions,
    /// <see cref="FormatException"/> for unparseable content.</summary>
    public ImportedFile Import(Stream content, string fileName)
    {
        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        var importer = _importers.FirstOrDefault(i => i.Extensions.Contains(ext))
            ?? throw new NotSupportedException(
                $"Unsupported file type '{ext}'. Supported: {string.Join(", ", SupportedExtensions)}");
        return importer.Import(content, fileName);
    }
}
