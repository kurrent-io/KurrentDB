using Polly;

namespace EventStore.Streaming.Consumers.Checkpoints;

class ResilientCheckpointStore(ICheckpointStore innerStore, ResiliencePipeline pipeline) : ICheckpointStore {
	ICheckpointStore   InnerStore { get; } = innerStore;
	ResiliencePipeline Pipeline   { get; } = pipeline;

	public string GroupId            => InnerStore.GroupId;
	public string ConsumerId         => InnerStore.ConsumerId;
	public string CheckpointStreamId => InnerStore.CheckpointStreamId;
	public bool   Initialized        => InnerStore.Initialized;
	
	public async Task Initialize(CancellationToken cancellationToken = default) {
		var resilienceContext = ResilienceContextPool.Shared
			.Get("checkpoint-store-initialize", cancellationToken);
				
		try {
			await Pipeline.ExecuteAsync(
				static async (ctx, store) => await store.Initialize(ctx.CancellationToken),
				resilienceContext, InnerStore
			);
		}
		finally {
			ResilienceContextPool.Shared.Return(resilienceContext);
		}
	}
	
	public async Task<RecordPosition[]> GetLatestPositions(CancellationToken cancellationToken = default) {
		var resilienceContext = ResilienceContextPool.Shared
			.Get("checkpoint-store-get-latest-positions", cancellationToken);
				
		try {
			return await Pipeline.ExecuteAsync(
				static async (ctx, store) => await store.GetLatestPositions(ctx.CancellationToken),
				resilienceContext, InnerStore
			);
		}
		finally {
			ResilienceContextPool.Shared.Return(resilienceContext);
		}
	}
	
	public async Task<RecordPosition[]> CommitPositions(RecordPosition[] positions, CancellationToken cancellationToken = default) {
		var resilienceContext = ResilienceContextPool.Shared
			.Get("checkpoint-store-commit-positions", cancellationToken);
				
		try {
			return await Pipeline.ExecuteAsync(
				static async (ctx, state) => await state.Store.CommitPositions(state.Positions, ctx.CancellationToken),
				resilienceContext, (Store: InnerStore, Positions: positions)
			);
		}
		finally {
			ResilienceContextPool.Shared.Return(resilienceContext);
		}
	}
}