// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Checker;

public sealed record GossipMember {
	public Guid InstanceId { get; init; }
	public string State { get; init; } = "Unknown";
	public bool IsAlive { get; init; }
	public string HttpEndPointIp { get; init; } = "";
	public int HttpEndPointPort { get; init; }
	public int NodePriority { get; init; }

	[JsonIgnore]
	public string Address => $"{HttpEndPointIp}:{HttpEndPointPort}";
}

public sealed record GossipView {
	public GossipMember[] Members { get; init; } = [];

	public GossipMember? Leader => Array.Find(Members, m => m.IsAlive && m.State == "Leader");

	public IEnumerable<GossipMember> Followers => Members.Where(m =>
		m.IsAlive && m.State is "Follower" or "Clone" or "CatchingUp");
}

/// <summary>
/// One leadership interval: who led, and from when to when.
/// </summary>
public sealed record LeadershipSpan(string? Leader, TimeSpan From) {
	public TimeSpan To { get; set; }
	public TimeSpan Duration => To - From;
}

/// <summary>
/// Polls every node's /gossip, so that a node being down or frozen never blinds the checker,
/// and records the leadership timeline the liveness invariants are computed from.
/// </summary>
public sealed class ClusterProbe : IDisposable {
	private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

	private readonly HttpClient[] _clients;
	private readonly string[] _addresses;
	private readonly Stopwatch _clock;
	private readonly List<LeadershipSpan> _timeline = [];
	private readonly Lock _gate = new();

	public ClusterProbe(string[] addresses, string scheme, string user, string password, Stopwatch clock) {
		_addresses = addresses;
		_clock = clock;

		var auth = new AuthenticationHeaderValue(
			"Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{user}:{password}")));

		_clients = addresses.Select(address => {
			var handler = new HttpClientHandler {
				ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
			};
			return new HttpClient(handler) {
				BaseAddress = new Uri($"{scheme}://{address}"),
				Timeout = TimeSpan.FromSeconds(2),
				DefaultRequestHeaders = { Authorization = auth },
			};
		}).ToArray();
	}

	public int ResignCount { get; private set; }
	public int ResignFailureCount { get; private set; }
	public int GossipFailureCount { get; private set; }

	public IReadOnlyList<LeadershipSpan> Timeline {
		get { lock (_gate) return [.. _timeline]; }
	}

	/// <summary>
	/// Asks every node at once and prefers an answer that names a live leader. A node that has just
	/// been frozen still reports its stale view, so taking the first response would invent outages.
	/// </summary>
	public async Task<GossipView?> ReadGossipAsync(CancellationToken token) {
		var views = await Task.WhenAll(_clients.Select(c => ReadOneAsync(c, token)));
		var withLeader = Array.Find(views, v => v?.Leader is not null);
		if (withLeader is not null)
			return withLeader;

		var any = Array.Find(views, v => v is not null);
		if (any is null)
			GossipFailureCount++;

		return any;
	}

	private static async Task<GossipView?> ReadOneAsync(HttpClient client, CancellationToken token) {
		try {
			var response = await client.GetAsync("/gossip", token);
			if (!response.IsSuccessStatusCode)
				return null;

			await using var stream = await response.Content.ReadAsStreamAsync(token);
			return await JsonSerializer.DeserializeAsync<GossipView>(stream, Json, token);
		} catch {
			return null;
		}
	}

	/// <summary>
	/// Samples the cluster continuously and stitches the samples into a leadership timeline.
	/// A span with a null leader is an outage.
	/// </summary>
	public async Task PollAsync(TimeSpan interval, CancellationToken token) {
		while (!token.IsCancellationRequested) {
			var view = await ReadGossipAsync(token);
			Record(view?.Leader?.Address);

			try {
				await Task.Delay(interval, token);
			} catch (OperationCanceledException) {
				break;
			}
		}

		Record(null, close: true);
	}

	private void Record(string? leader, bool close = false) {
		lock (_gate) {
			var now = _clock.Elapsed;
			var current = _timeline.Count > 0 ? _timeline[^1] : null;

			if (current is not null && current.Leader == leader && !close) {
				current.To = now;
				return;
			}

			if (current is not null)
				current.To = now;

			if (!close)
				_timeline.Add(new LeadershipSpan(leader, now) { To = now });
		}
	}

	/// <summary>
	/// Pumba can crash and freeze a node but cannot ask it to resign - that is an application
	/// operation, so it stays here.
	/// </summary>
	public async Task ResignLeadersAsync(TimeSpan interval, CancellationToken token) {
		while (!token.IsCancellationRequested) {
			try {
				await Task.Delay(interval, token);
			} catch (OperationCanceledException) {
				return;
			}

			var view = await ReadGossipAsync(token);
			if (view?.Leader is not { } leader)
				continue;

			var index = Array.IndexOf(_addresses, leader.Address);
			if (index < 0) {
				Console.WriteLine($"[chaos] leader {leader.Address} is not a configured node, skipping resign");
				continue;
			}

			try {
				var response = await _clients[index].PostAsync("/admin/node/resign", content: null, token);
				if (response.IsSuccessStatusCode) {
					ResignCount++;
					Console.WriteLine($"[chaos] resigned leader {leader.Address}");
				} else {
					ResignFailureCount++;
					Console.WriteLine($"[chaos] resign of {leader.Address} returned {(int)response.StatusCode}");
				}
			} catch (Exception ex) {
				ResignFailureCount++;
				Console.WriteLine($"[chaos] resign of {leader.Address} failed: {ex.Message}");
			}
		}
	}

	public void Dispose() {
		foreach (var client in _clients)
			client.Dispose();
	}
}
