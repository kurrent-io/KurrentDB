
namespace Kurrent.Kontext.Mcp.Model;

public sealed class RecallOptions {
    public string? QueryId { get; set; }

    public int Limit { get; set; }

    public double MinScore { get; set; }

    public IReadOnlyList<Tag> Tags { get; set; } = [];

    public bool IncludeFull { get; set; }
}