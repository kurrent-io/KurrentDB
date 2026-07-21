
namespace Kurrent.Kontext.Mcp.Model;

public sealed class ReflectOptions {
    public string? QueryId { get; set; }

    public IReadOnlyList<Tag> Tags { get; set; } = [];
}