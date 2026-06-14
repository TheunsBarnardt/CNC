using Backend.Models;

namespace Backend.Import;

public sealed class PdfFileImporter : IFileImporter
{
    public IReadOnlySet<string> Extensions { get; } = new HashSet<string> { ".pdf" };

    public ImportedFile Import(Stream content, string fileName) =>
        PdfImporter.Import(content, fileName);
}
