// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Kontext.Mcp;

internal static class ServerInstructions {
	public const string Text =
		"""
        kontext is an agent memory & RAG system. Use it when you need to
        remember something from the conversation, search for information, or
        import data.

        Glossary:
        - Stream. A named, ordered, append-only sequence of events.
        - Event. An immutable record in a stream.
        - Workspace. An isolated scope with its own memory and event index.
        - Topic. The unique identifier for a fact in memory; retaining
          to the same topic overwrites.
        - Inquiry. A working set of events gathered to ground one line of investigation.

        Tool categories (by name prefix):
        - ws_*  (Workspace) — inspect indexing state, list indexed streams,
          import external data as events, manage workspaces.
        - mem_* (Memory) — durable facts that survive across chat sessions.
        - inq_* (Inquiries) — open an inquiry, search/read raw events as
          grounding, close when done.

        Workspace tools:
        - ws_status — check indexing progress; if 'Catching up', wait or
          proceed with partial data.
        - ws_streams — discover streams.
        - ws_import — load external data as events into the current
          workspace.
        - ws_management — curl commands (require admin access) to list,
          create, start, stop, or delete workspaces.

        Memory workflow:
        - mem_recall whenever relevant context might exist (not just at
          startup). Recalled facts may be outdated — verify against raw
          events when accuracy matters.
        - mem_topics → mem_retain: find an existing related topic and
          reuse it (overwrites), or pick a fresh topic for a distinct
          new fact. Save insights immediately as you discover them.

        Inquiries workflow:
        - ws_status first.
        - mem_recall to surface any existing facts on the topic.
        - inq_new → inq_search / inq_read to gather raw events as grounding;
          inq_forget drops noise, inq_view reviews the working set;
          inq_end when done.
        """;
}