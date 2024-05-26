using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace EventStore.Streaming.Persistence.State;

/// <summary>
///   A state store that uses a distributed cache as the backing store.
/// </summary>
/// <param name="cache"></param>
/// <param name="entryOptions"></param>
/// <param name="loggerFactory"></param>
public class DistributedStateStore(IDistributedCache cache, DistributedCacheEntryOptions? entryOptions, ILoggerFactory loggerFactory) : IStateStore {
	public DistributedStateStore(IDistributedCache cache, ILoggerFactory loggerFactory) : this(cache, null, loggerFactory) { }

	ILogger                      Logger       { get; } = loggerFactory.CreateLogger<DistributedStateStore>();
	IDistributedCache            Cache        { get; } = cache;
	DistributedCacheEntryOptions EntryOptions { get; } = entryOptions ?? new DistributedCacheEntryOptions();

	public async ValueTask<T> Set<T>(object key, T value, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(key);

		try {
			await Cache
				.SetAsync(key.ToString()!, JsonSerializer.SerializeToUtf8Bytes(value), EntryOptions, cancellationToken)
				.ConfigureAwait(false);

			return value;
		}
		catch (Exception ex) {
			Logger.LogError(ex, "Failed to set state: {Key}", key);
			throw;
		}
	}

	public async ValueTask<T?> Get<T>(object key, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(key);

        try {
	        var bytes = await Cache
		        .GetAsync(key.ToString()!, cancellationToken)
		        .ConfigureAwait(false);

	        return JsonSerializer.Deserialize<T>(bytes);
        }
        catch (Exception ex) {
	        Logger.LogError(ex, "Failed to get state: {Key}", key);
	        throw;
        }
	}

	public async ValueTask Delete<T>(object key, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(key);

        try {
	        await Cache
		        .RemoveAsync(key.ToString()!, cancellationToken)
		        .ConfigureAwait(false);
        }
        catch (Exception ex) {
	        Logger.LogError(ex, "Failed to delete state: {Key}", key);
	        throw;
        }
	}

	public async ValueTask<T?> GetOrSet<T>(object key, Func<ValueTask<T?>> factory, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(key);

        var cacheKey = key.ToString()!;

		try {
			var data = await Cache
				.GetAsync(cacheKey, cancellationToken)
				.ConfigureAwait(false);

			if (data?.Length > 0)
				return JsonSerializer.Deserialize<T>(data);
		}
		catch (Exception ex) {
			Logger.LogError(ex, "Failed to get state: {Key}", key);
			throw;
		}

		T? value;

		try {
			value = await factory().ConfigureAwait(false);
		}
		catch (Exception ex) {
			Logger.LogError(ex, "Failed to create state: {Key}", key);
			throw;
		}

		if (value is not null) {
			try {
				await Cache
					.SetAsync(cacheKey, JsonSerializer.SerializeToUtf8Bytes(value), EntryOptions, cancellationToken)
					.ConfigureAwait(false);
			}
			catch (Exception ex) {
				Logger.LogError(ex, "Failed to set state: {Key}", key);
				throw;
			}
		}

		return value;
	}
}