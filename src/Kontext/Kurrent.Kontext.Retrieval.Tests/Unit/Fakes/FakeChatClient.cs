// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.AI;

namespace Kurrent.Kontext.Retrieval.Tests.Fakes;

sealed class FakeChatClient : IChatClient {
	readonly Func<string, string> reply;
	readonly Lock                 gate = new();
	readonly List<string>         prompts = [];
	readonly List<ChatOptions?>   options = [];

	public FakeChatClient(params string[] replies) {
		if (replies.Length == 0)
			throw new ArgumentException("A FakeChatClient needs at least one reply.", nameof(replies));

		var served = 0;
		reply = _ => replies[Math.Min(Interlocked.Increment(ref served) - 1, replies.Length - 1)];
	}

	public FakeChatClient(Func<string, string> replyToPrompt) =>
		reply = replyToPrompt;

	public IReadOnlyList<string> Prompts => prompts;

	public IReadOnlyList<ChatOptions?> OptionsSeen => options;

	public ChatOptions? LastOptions => options.Count > 0 ? options[^1] : null;

	public int Calls => prompts.Count;

	public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? chatOptions = null, CancellationToken ct = default) {
		var prompt = string.Join("\n", messages.Select(message => message.Text));

		lock (gate) {
			prompts.Add(prompt);
			options.Add(chatOptions);
		}

		return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply(prompt))));
	}

	public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? chatOptions = null, CancellationToken ct = default) =>
		throw new NotSupportedException("FakeChatClient does not stream.");

	public object? GetService(System.Type serviceType, object? serviceKey = null) =>
		throw new NotSupportedException("FakeChatClient exposes no services.");

	public void Dispose() { }
}
