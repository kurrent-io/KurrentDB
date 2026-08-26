using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Kurrent.Kontext.Memory.Mcp;

/// <summary>
/// MCP prompts that teach a client the correct way to use Kontext.
///
/// The prompt bodies are written in Simplified Technical English (ASD-STE100):
/// short sentences, the imperative mood, active voice, one instruction per
/// sentence, and a small, consistent vocabulary.
/// </summary>
[McpServerPromptType]
public class MemoryPrompts {
    [McpServerPrompt(Name = "kontext_overview", Title = "How to use Kontext")]
    [Description("Explain the correct way to use Kontext, including its structure and rules.")]
    public string KontextOverview() =>
        """
        Kontext is your long-term memory. It keeps memories between sessions.

        Kontext has three levels:
        1. A workspace is a top-level area. Examples: Engineering, Sales.
        2. A project is inside a workspace. A project has a goal.
        3. A memory is a fact, a note, or a decision. A memory belongs to a project or a workspace.

        Obey these rules:
        - Search before you store. Do not make duplicate memories.
        - Give each memory a short, clear title.
        - Add two or more tags to each memory.
        - Put each memory in the correct project or workspace.
        - Load related memories before you answer.
        - Update a memory when the facts change. Do not add a second memory for the same fact.
        """;

    [McpServerPrompt(Name = "retain_memory", Title = "Retain a memory correctly"),
     Description("Give the correct steps to retain a new memory without making duplicates.")]
    public string RetainMemory([Description("Optional summary of the fact or note to store.")] string? content = null) =>
        $"""
        Do these steps to store a memory{(string.IsNullOrWhiteSpace(content) ? "" : $" about: {content}")}.

        1. Search for related memories first. Use the search_memories tool.
        2. If a related memory exists, update it with the edit tool. Then stop.
        3. If no related memory exists, continue.
        4. Choose the owner. Use a project for work items. Use a workspace for general facts.
        5. Write a short, clear title.
        6. Add two or more tags.
        7. Store the memory with the store tool.
        8. If the memory relates to another memory, link them with the create_reference tool.
        """;

    [McpServerPrompt(Name = "gather_context", Title = "Find context before you answer"),
     Description("Give the correct steps to find and load memories before you answer a question.")]
    public string GatherContext([Description("The subject of the question.")] string topic) =>
        $"""
        Do these steps before you answer a question about {topic}.

        1. Search for memories with the search_memories tool. Use "{topic}" as the query.
        2. Read the top results.
        3. Load the full text of the useful memories. Use the get tool or the get_many tool.
        4. If the question is about a project, load the project context. Use the get_project_context tool.
        5. Use the memories in your answer.
        6. If you find no memory, tell the user. Then answer from general knowledge.
        """;

    [McpServerPrompt(Name = "maintain_memory", Title = "Keep memories correct and current"),
     Description("Give the correct steps to keep memories correct, current, and clean.")]
    public string MaintainMemory() =>
        """
        Do these steps to keep memories correct.

        1. When facts change, edit the memory with the edit tool. Do not make a new memory.
        2. When a memory is old or not useful, archive it with the archive_memory tool.
        3. Do not delete a memory unless it is a mistake. Archive is safer than delete.
        4. To restore an archived memory, use the restore_memory tool.
        5. To undo a bad change, revert the memory to an earlier version with the revert_to_version tool.
        """;
}