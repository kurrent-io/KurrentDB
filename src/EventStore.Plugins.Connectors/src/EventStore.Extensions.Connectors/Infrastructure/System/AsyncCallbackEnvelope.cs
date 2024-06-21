// ReSharper disable CheckNamespace

using EventStore.Core.Messaging;

namespace EventStore.Core;

class AsyncCallbackEnvelope(Func<Message, Task> callback) : IEnvelope {
	Func<Message, Task> Callback { get; } = callback;

	public void ReplyWith<T>(T message) where T : Message => Callback(message).ConfigureAwait(true).GetAwaiter().GetResult();

	public static AsyncCallbackEnvelope Create(Func<Message, Task> callback) => new(callback);
}