namespace EventStore.Streaming;

delegate Task OnPeriodicTimerTick(bool isLastTick, CancellationToken cancellationToken);

[PublicAPI]
sealed class AsyncPeriodicTimer {
	public AsyncPeriodicTimer(TimeSpan period, OnPeriodicTimerTick onTick) {
		Period    = period;
		OnTick    = onTick;
		LazyTimer = new(() => new PeriodicTimer(Period));
	}

	Lazy<PeriodicTimer> LazyTimer { get; }
	TimeSpan            Period    { get; }
	OnPeriodicTimerTick OnTick    { get; }

	PeriodicTimer Timer => LazyTimer.Value;
	
	public void Start(CancellationToken stoppingToken = default) {
		if (LazyTimer.IsValueCreated)
			return;

		_ = TickTock();
		
		return;

		async Task TickTock() {
			try {
				while (await Timer.WaitForNextTickAsync(stoppingToken))
					await OnTick(false, stoppingToken);
			}
			catch (OperationCanceledException) {
				await OnTick(true, stoppingToken);
			}
		}
	}
   
	public void Stop() => Timer.Dispose();
}

//
// [PublicAPI]
// sealed class AsyncPeriodicTimer : IAsyncDisposable {
// 	public AsyncPeriodicTimer(TimeSpan period, OnPeriodicTimerTick onTick) {
// 		Period    = period;
// 		OnTick    = onTick;
// 		LazyTimer = new(() => new PeriodicTimer(Period));
// 	}
//
// 	Lazy<PeriodicTimer> LazyTimer { get; }
// 	TimeSpan            Period    { get; }
// 	OnPeriodicTimerTick OnTick    { get; }
//
// 	PeriodicTimer Timer => LazyTimer.Value;
//
// 	public long TickCount { get; private set; }
// 	
// 	public void ProperStart(CancellationToken stoppingToken = default) {
// 		_ = TickTock();
//         
// 		async Task TickTock() {
// 			while (!stoppingToken.IsCancellationRequested && await Timer.WaitForNextTickAsync(stoppingToken)) {
// 				TickCount++;
// 				await OnTick(false, stoppingToken);
// 			}
//
// 			TickCount++;
// 			await OnTick(true, stoppingToken);
// 			
// 			Timer.Dispose();
// 		}
// 	}
//    
// 	//public void Stop() => Timer.Dispose();
//
// 	public Task Start(CancellationToken stoppingToken = default) {
// 		_ = TickTock();
//         
// 		return Task.CompletedTask;
//         
// 		async Task TickTock() {
// 			while (!stoppingToken.IsCancellationRequested && await Timer.WaitForNextTickAsync(stoppingToken)) {
// 				TickCount++;
// 				await OnTick(false, stoppingToken);
// 			}
//
// 			TickCount++;
// 			await OnTick(true, stoppingToken);
// 		}
// 	}
//    
// 	public ValueTask Stop() {
// 		Timer.Dispose();
// 		return ValueTask.CompletedTask;
// 	}
//     
// 	public ValueTask DisposeAsync() => Stop();
// }


// [PublicAPI]
// sealed class AsyncPeriodicTimer : IAsyncDisposable {
// 	public AsyncPeriodicTimer(TimeSpan period, OnPeriodicTimerTick onTick) {
// 		Period    = period;
// 		OnTick    = onTick;
// 		LazyTimer = new(() => new PeriodicTimer(Period));
// 	}
// 	
// 	Lazy<PeriodicTimer> LazyTimer { get; }
// 	
// 	TimeSpan            Period { get; }
// 	OnPeriodicTimerTick OnTick { get; }
//
// 	PeriodicTimer? Timer { get; set; }
//
// 	public long TickCount { get; }
// 	
// 	public Task Start(CancellationToken stoppingToken = default) {
// 		_ = TickTock();
// 		return Task.CompletedTask;
//         
// 		async Task TickTock() {
// 			while (!stoppingToken.IsCancellationRequested && await Timer.WaitForNextTickAsync(stoppingToken)) {
// 				TickCount++;
// 				await OnTick(false, stoppingToken);
// 			}
//
// 			TickCount++;
// 			await OnTick(true, stoppingToken);
// 		}
// 	}
//     
// 	public ValueTask DisposeAsync() {
// 		Timer?.Dispose();
// 		return ValueTask.CompletedTask;
// 	}
// }