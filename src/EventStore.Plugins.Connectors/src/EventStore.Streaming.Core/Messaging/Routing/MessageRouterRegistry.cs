using System.Collections.Frozen;

namespace EventStore.Streaming.Routing;

using HotHandlers = Dictionary<Route, HashSet<IMessageHandler>>;
using ColdHandlers = FrozenDictionary<Route, IReadOnlyCollection<IMessageHandler>>;

[PublicAPI]
public class MessageRouterRegistry {
    HotHandlers  HotHandlers  { get; }      = [];
    ColdHandlers ColdHandlers { get; set; } = ColdHandlers.Empty;

    public IReadOnlyCollection<Endpoint> Endpoints => 
		ColdHandlers.Select(x => new Endpoint(x.Key, x.Value)).ToArray();

	object Locker { get; } = new();

	public void RegisterHandler(Route route, IMessageHandler handler) {
		lock (Locker) {
			if (!HotHandlers.ContainsKey(route))
				HotHandlers[route] = [];

			if (!HotHandlers[route].Add(handler))
				throw new($"This handler was already registered to {route}");

			// don't care about performance on registration... this is way simpler.
			ColdHandlers = HotHandlers
				.ToFrozenDictionary(x => x.Key, x => (IReadOnlyCollection<IMessageHandler>)x.Value);
		}
	}

	public IEnumerable<IMessageHandler> GetHandlers(params Route[] routes) =>
		routes.SelectMany(
			route => ColdHandlers.TryGetValue(route, out var handlers)
				? handlers
				: Array.Empty<IMessageHandler>()
		);

	public bool ContainsRoute(params Route[] routes) => 
		routes.Any(ColdHandlers.ContainsKey);

	public MessageRouterRegistry Combine(MessageRouterRegistry registry) {
		foreach (var (route, handlers) in registry.Endpoints)
		foreach (var handler in handlers)
			RegisterHandler(route, handler);

		return this;
	}
}