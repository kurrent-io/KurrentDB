namespace EventStore.Connectors.Infrastructure.Diagnostics;

class GenericObserver<T>(Action<T>? onNext, Action? onCompleted = null) : IObserver<T> {
	readonly Action _onCompleted = onCompleted ?? (() => { });
	readonly Action<T> _onNext = onNext ?? (_ => { });

	public void OnNext(T value) => _onNext(value);
	public void OnCompleted() => _onCompleted();
	public void OnError(Exception error) { }
}