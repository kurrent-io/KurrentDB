
namespace Kurrent.Kontext.Memory.Mcp.Model;

public sealed class ReflectOptions {
    public string? QueryId { get; set; }

    public IReadOnlyList<Tag> Tags { get; set; } = [];
}