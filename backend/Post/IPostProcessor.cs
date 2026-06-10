using Backend.Cam;
using Backend.Models;

namespace Backend.Post;

/// <summary>
/// Turns the neutral toolpath into a controller dialect. Adding a controller
/// (laser post, vinyl post, Marlin, ...) = adding an implementation and
/// registering it in DI — the CAM engine is never touched.
/// </summary>
public interface IPostProcessor
{
    /// <summary>Stable identifier used by the API ("grbl-plasma").</summary>
    string Id { get; }

    string DisplayName { get; }

    /// <summary>Description shown in the UI (target controller, conventions).</summary>
    string Description { get; }

    /// <summary>Preferred output extension, with the dot (".nc").</summary>
    string FileExtension { get; }

    GcodeProgram Generate(Toolpath toolpath, Project project);
}

/// <summary>The result of post-processing: G-code lines plus any warnings.</summary>
public sealed class GcodeProgram
{
    public List<string> Lines { get; init; } = [];
    public List<string> Warnings { get; init; } = [];

    public string ToText() => string.Join("\n", Lines) + "\n";
}
