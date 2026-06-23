// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;
using KurrentDB.Components.Shared;
using KurrentDB.SecondaryIndexing.Stats;
using static KurrentDB.SecondaryIndexing.Stats.StatsSql;

namespace KurrentDB.Components.Stats;

// UI-layer wrapper over the SecondaryIndexing StatsService. That service reads the secondary index
// directly and authorizes nothing. This wrapper gives Stats the same defense-in-depth every other
// UI service has (via the IAuthorizationProvider.EnsureAccessAsync extension): it enforces the
// configured policy (read $all — the same Operation the ViewStats page policy and the FlightSql query
// path use) before each query, so the page-level [Authorize] is no longer the only gate. The expensive,
// synchronous DuckDB work is offloaded to the thread pool so the render thread isn't blocked.
public sealed class UiStatsService(StatsService inner, IAuthorizationProvider authorizer) {
	static readonly Operation ReadAllOperation = UiOperations.ReadAll;

	public async Task<IReadOnlyList<CategoryName>> GetCategoriesAsync(ClaimsPrincipal principal, CancellationToken ct = default) {
		await authorizer.EnsureAccessAsync(principal, ReadAllOperation, ct);
		return await Task.Run(() => inner.GetCategories().ToList(), ct);
	}

	public async Task<IReadOnlyList<GetCategoryStats.Result>> GetCategoryStatsAsync(ClaimsPrincipal principal, string category, CancellationToken ct = default) {
		await authorizer.EnsureAccessAsync(principal, ReadAllOperation, ct);
		return await Task.Run(() => inner.GetCategoryStats(category), ct);
	}

	public async Task<IReadOnlyList<GetCategoryEventTypes.Result>> GetCategoryEventTypesAsync(ClaimsPrincipal principal, string category, CancellationToken ct = default) {
		await authorizer.EnsureAccessAsync(principal, ReadAllOperation, ct);
		return await Task.Run(() => inner.GetCategoryEventTypes(category), ct);
	}

	public async Task<IReadOnlyList<GetExplicitTransactions.Result>> GetExplicitTransactionsAsync(ClaimsPrincipal principal, CancellationToken ct = default) {
		await authorizer.EnsureAccessAsync(principal, ReadAllOperation, ct);
		return await Task.Run(() => inner.GetExplicitTransactions(), ct);
	}

	public async Task<List<GetLongestStreams.Result>> GetLongestStreamsAsync(ClaimsPrincipal principal, CancellationToken ct = default) {
		await authorizer.EnsureAccessAsync(principal, ReadAllOperation, ct);
		return await Task.Run(() => inner.GetLongestStreams(), ct);
	}
}
