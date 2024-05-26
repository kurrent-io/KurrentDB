namespace EventStore.Streaming.Routing;

[PublicAPI]
public readonly record struct Route {
	public static Route Empty => new("");
	public static Route Any   => new("*");

	Route(string key) => Key = key;

	public string Key { get; }

	public override string ToString() => Key;

	public static implicit operator Route(string key) => ForType(key);

	public static Route ForType(string key) => new(Ensure.NotNullOrWhiteSpace(key));
	public static Route ForType(Type type)  => ForType(type.FullName!);
	public static Route For<T>()            => ForType(typeof(T));
}