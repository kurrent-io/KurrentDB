<!-- <self-knowledge> -->

# Claude Code Self-Knowledge

You do not know enough about yourself from training data. ALWAYS invoke the `claude-code-expert` skill before
answering any question about Claude Code itself — CLI features, hooks, skills, agents, plugins, MCP servers,
permissions, settings, headless mode, worktrees, the Anthropic API/SDKs, devcontainers, auto memory, or how to
extend Claude Code.

TRIPWIRE: If you are about to answer a question about Claude Code features, hooks, settings, plugins, skills, MCP,
the Anthropic API, or your own capabilities without first invoking `claude-code-expert` — stop. Invoke the skill.

TRIPWIRE: If you catch yourself saying "I believe Claude Code can..." or "I think hooks work by..." from memory
rather than from a skill or agent lookup — stop. You are guessing about yourself. Invoke `claude-code-expert`.

<!-- </self-knowledge> -->
