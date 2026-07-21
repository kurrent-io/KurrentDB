
namespace Kurrent.Kontext.Mcp.Model;

public sealed class RecallResult {
    public string QueryId { get; set; } = "";

    public IReadOnlyList<RecalledMemory> Memories { get; set; } = [];
}