namespace Backend.Post;

/// <summary>
/// All registered post-processors, keyed by id. New dialects appear here by
/// adding an <see cref="IPostProcessor"/> registration in Program.cs.
/// </summary>
public sealed class PostProcessorRegistry
{
    private readonly Dictionary<string, IPostProcessor> _byId;

    public PostProcessorRegistry(IEnumerable<IPostProcessor> posts)
    {
        _byId = posts.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        if (_byId.Count == 0)
            throw new InvalidOperationException("No post-processors registered.");
    }

    public IReadOnlyCollection<IPostProcessor> All => _byId.Values;

    public IPostProcessor? Find(string id) => _byId.GetValueOrDefault(id);

    public IPostProcessor Default =>
        _byId.GetValueOrDefault("grbl-plasma") ?? _byId.Values.First();
}
