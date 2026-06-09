using Backend.Models;

namespace Backend.Import;

/// <summary>
/// Parses one vector file format into the neutral geometry model. New formats
/// are added by adding an implementation — callers go through
/// <see cref="FileImportService"/> and never know the format.
/// </summary>
public interface IFileImporter
{
    /// <summary>Lower-case extensions this importer handles (e.g. ".svg").</summary>
    IReadOnlySet<string> Extensions { get; }

    /// <summary>
    /// Parse <paramref name="content"/> into geometry. Throws
    /// <see cref="FormatException"/> for unparseable files; recoverable issues
    /// (skipped entities) go into <see cref="ImportedFile.Warnings"/>.
    /// </summary>
    ImportedFile Import(Stream content, string fileName);
}
