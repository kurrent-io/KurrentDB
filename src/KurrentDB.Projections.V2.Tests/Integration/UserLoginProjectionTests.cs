// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using EventStore.Client.Projections;
using Google.Protobuf;
using Grpc.Core;
using KurrentDB.Projections.V2.Tests.Fixtures;
using KurrentDB.Protocol.V2.Streams;
using SchemaFormat = KurrentDB.Protocol.V2.Streams.SchemaFormat;
using SchemaInfo = KurrentDB.Protocol.V2.Streams.SchemaInfo;

namespace KurrentDB.Projections.V2.Tests.Integration;

/// <summary>
/// End-to-end projection tests using a user registration/login domain.
/// Tests both V1 and V2 engines via the management gRPC API.
/// </summary>
[NotInParallel]
public class UserLoginProjectionTests {
	const int LockoutThreshold = 3;

	[ClassDataSource<ProjectionsNodeFixture>(Shared = SharedType.PerTestSession)]
	public required ProjectionsNodeFixture Fixture { get; init; }

	#region Projection Source

	/// <summary>
	/// Projection that tracks per-user login state and emits lockout events.
	/// Uses fromCategory("user").foreachStream() to partition by user stream.
	/// </summary>
	static string GetProjectionSource(string category) => $$"""
		fromCategory("{{category}}")
		.foreachStream()
		.when({
			$init: function() {
				return { successfulLogins: 0, failedLogins: 0, lockedOut: false };
			},
			UserRegistered: function(s, e) {
				return s;
			},
			UserLoggedIn: function(s, e) {
				s.successfulLogins++;
				s.failedLogins = 0;
				return s;
			},
			UserLoginFailed: function(s, e) {
				s.failedLogins++;
				if (s.failedLogins >= {{LockoutThreshold}} && !s.lockedOut) {
					s.lockedOut = true;
					emit("lockouts-{{category}}", "UserLockedOut", { userId: e.streamId });
				}
				return s;
			},
			UserLockedOut: function(s, e) {
				s.lockedOut = true;
				return s;
			}
		});
		""";

	#endregion

	#region Data Generation

	record UserScenario(string UserId, List<(string EventType, string Json)> Events, int ExpectedSuccessful, int ExpectedFailed, bool ExpectedLockedOut);

	static int TotalEventCount(List<UserScenario> scenarios) => scenarios.Sum(s => s.Events.Count);

	/// <summary>
	/// Generates test scenarios for the given number of users.
	/// Some users have only successful logins, some have failed attempts,
	/// and some hit the lockout threshold.
	/// </summary>
	static List<UserScenario> GenerateUserScenarios(string category, int userCount, int seed = 42) {
		var rng = new Random(seed);
		var scenarios = new List<UserScenario>();

		for (int i = 0; i < userCount; i++) {
			var userId = $"{category}-user{i:D4}";
			var events = new List<(string EventType, string Json)>();
			var successfulLogins = 0;
			var consecutiveFails = 0;
			var lockedOut = false;

			// Every user starts with registration
			events.Add(("UserRegistered", JsonSerializer.Serialize(new { username = $"user{i:D4}", email = $"user{i}@test.com" })));

			// Determine user pattern based on index for determinism
			var pattern = i % 5;
			switch (pattern) {
				case 0: // Happy user: only successful logins
					for (int j = 0; j < 3 + rng.Next(3); j++) {
						events.Add(("UserLoggedIn", JsonSerializer.Serialize(new { timestamp = DateTime.UtcNow.AddMinutes(j).ToString("O") })));
						successfulLogins++;
						consecutiveFails = 0;
					}
					break;

				case 1: // User with some failed attempts but recovers
					events.Add(("UserLoginFailed", JsonSerializer.Serialize(new { reason = "wrong_password" })));
					events.Add(("UserLoginFailed", JsonSerializer.Serialize(new { reason = "wrong_password" })));
					events.Add(("UserLoggedIn", JsonSerializer.Serialize(new { timestamp = DateTime.UtcNow.ToString("O") })));
					successfulLogins++;
					consecutiveFails = 0;
					break;

				case 2: // User gets locked out
					for (int j = 0; j < LockoutThreshold; j++) {
						events.Add(("UserLoginFailed", JsonSerializer.Serialize(new { reason = "wrong_password" })));
						consecutiveFails++;
					}
					lockedOut = true;
					break;

				case 3: // User gets locked out then more events
					for (int j = 0; j < LockoutThreshold + 1; j++) {
						events.Add(("UserLoginFailed", JsonSerializer.Serialize(new { reason = "wrong_password" })));
						consecutiveFails++;
						if (consecutiveFails == LockoutThreshold) lockedOut = true;
					}
					break;

				case 4: // Mixed: fail, succeed, fail to lockout
					events.Add(("UserLoginFailed", JsonSerializer.Serialize(new { reason = "wrong_password" })));
					events.Add(("UserLoggedIn", JsonSerializer.Serialize(new { timestamp = DateTime.UtcNow.ToString("O") })));
					successfulLogins++;
					consecutiveFails = 0;
					// Now fail enough to lock out
					for (int j = 0; j < LockoutThreshold; j++) {
						events.Add(("UserLoginFailed", JsonSerializer.Serialize(new { reason = "wrong_password" })));
						consecutiveFails++;
					}
					lockedOut = true;
					break;
			}

			// The projection resets failedLogins on successful login, so
			// ExpectedFailed is the count of consecutive fails at the end
			scenarios.Add(new UserScenario(userId, events, successfulLogins, consecutiveFails, lockedOut));
		}

		return scenarios;
	}

	#endregion

	#region Helpers

	static AppendRequest CreateAppendRequest(string stream, params (string eventType, string json)[] events) {
		var request = new AppendRequest {
			Stream = stream,
			ExpectedRevision = -2 // Any
		};
		foreach (var (eventType, json) in events) {
			request.Records.Add(new AppendRecord {
				RecordId = Guid.NewGuid().ToString(),
				Data = ByteString.CopyFromUtf8(json),
				Schema = new SchemaInfo {
					Name = eventType,
					Format = SchemaFormat.Json
				}
			});
		}
		return request;
	}

	async Task AppendUserEvents(List<UserScenario> scenarios, CancellationToken ct) {
		foreach (var scenario in scenarios) {
			await Fixture.StreamsClient.AppendAsync(
				CreateAppendRequest(scenario.UserId, scenario.Events.ToArray()), ct);
		}
	}

	async Task CreateProjection(string name, string source, int engineVersion, CancellationToken ct) {
		await Fixture.ProjectionsClient.CreateAsync(new CreateReq {
			Options = new CreateReq.Types.Options {
				Continuous = new CreateReq.Types.Options.Types.Continuous {
					Name = name,
					EmitEnabled = true,
					TrackEmittedStreams = false
				},
				Query = source,
				EngineVersion = engineVersion
			}
		}, cancellationToken: ct);
	}

	async Task DisableProjection(string name, CancellationToken ct) {
		await Fixture.ProjectionsClient.DisableAsync(new DisableReq {
			Options = new DisableReq.Types.Options {
				Name = name,
				WriteCheckpoint = true
			}
		}, cancellationToken: ct);
	}

	async Task EnableProjection(string name, CancellationToken ct) {
		await Fixture.ProjectionsClient.EnableAsync(new EnableReq {
			Options = new EnableReq.Types.Options {
				Name = name
			}
		}, cancellationToken: ct);
	}

	async Task WaitForProjectionStatus(string name, string expectedStatus, TimeSpan timeout, CancellationToken ct) {
		var deadline = DateTime.UtcNow + timeout;
		string lastStatus = "unknown";
		while (DateTime.UtcNow < deadline) {
			ct.ThrowIfCancellationRequested();
			try {
				var stats = Fixture.ProjectionsClient.Statistics(new StatisticsReq {
					Options = new StatisticsReq.Types.Options { Name = name }
				}, cancellationToken: ct);

				await foreach (var stat in stats.ResponseStream.ReadAllAsync(ct)) {
					lastStatus = stat.Details.Status;
					if (stat.Details.Status.Contains(expectedStatus, StringComparison.OrdinalIgnoreCase))
						return;
				}
			} catch (RpcException ex) {
				lastStatus = $"RpcException: {ex.StatusCode}";
			}
			await Task.Delay(1000, ct);
		}
		throw new TimeoutException($"Projection '{name}' did not reach status '{expectedStatus}' within {timeout}. Last status: '{lastStatus}'");
	}

	async Task WaitForProjectionToProcessAllEvents(string name, int expectedEventCount, TimeSpan timeout, CancellationToken ct) {
		// Wait until the projection has processed at least expectedEventCount events.
		var deadline = DateTime.UtcNow + timeout;
		string lastStatus = "unknown";

		while (DateTime.UtcNow < deadline) {
			ct.ThrowIfCancellationRequested();
			await Task.Delay(2000, ct);

			try {
				var stats = Fixture.ProjectionsClient.Statistics(new StatisticsReq {
					Options = new StatisticsReq.Types.Options { Name = name }
				}, cancellationToken: ct);

				await foreach (var stat in stats.ResponseStream.ReadAllAsync(ct)) {
					var eventsProcessed = stat.Details.EventsProcessedAfterRestart;
					lastStatus = $"{stat.Details.Status} events={eventsProcessed}/{expectedEventCount}";

					if (eventsProcessed >= expectedEventCount)
						return;
				}
			} catch (RpcException) {
				// Projection may not be ready yet
			}
		}
		throw new TimeoutException($"Projection '{name}' did not finish processing within {timeout}. Last: {lastStatus}");
	}

	async Task VerifyUserStates(string projectionName, List<UserScenario> scenarios, CancellationToken ct) {
		foreach (var scenario in scenarios) {
			// Extract stream suffix (the part after the category dash)
			var partition = scenario.UserId;

			var resp = await Fixture.ProjectionsClient.StateAsync(new StateReq {
				Options = new StateReq.Types.Options {
					Name = projectionName,
					Partition = partition
				}
			}, cancellationToken: ct);

			var stateJson = resp.State.StructValue;

			var successfulLogins = (int)stateJson.Fields["successfulLogins"].NumberValue;
			var failedLogins = (int)stateJson.Fields["failedLogins"].NumberValue;
			var lockedOut = stateJson.Fields["lockedOut"].BoolValue;

			await Assert.That(successfulLogins)
				.IsEqualTo(scenario.ExpectedSuccessful)
				.Because($"User {scenario.UserId} should have {scenario.ExpectedSuccessful} successful logins");

			await Assert.That(failedLogins)
				.IsEqualTo(scenario.ExpectedFailed)
				.Because($"User {scenario.UserId} should have {scenario.ExpectedFailed} consecutive failed logins");

			await Assert.That(lockedOut)
				.IsEqualTo(scenario.ExpectedLockedOut)
				.Because($"User {scenario.UserId} locked out should be {scenario.ExpectedLockedOut}");
		}
	}

	#endregion

	#region Tests — V1 Engine

	[Test]
	[Timeout(180_000)]
	public async Task v1_happy_path_projection_created_before_events(CancellationToken ct) {
		var testId = Guid.NewGuid().ToString("N")[..8];
		var category = $"u1a{testId}";
		var projectionName = $"user-login-v1-before-{testId}";
		var scenarios = GenerateUserScenarios(category, 100);

		// 1. Create projection first
		await CreateProjection(projectionName, GetProjectionSource(category), engineVersion: 1, ct);
		await WaitForProjectionStatus(projectionName, "Running", TimeSpan.FromSeconds(30), ct);

		// 2. Append events
		await AppendUserEvents(scenarios, ct);

		// 3. Wait for projection to catch up
		await Task.Delay(5000, ct);
		await WaitForProjectionToProcessAllEvents(projectionName, TotalEventCount(scenarios), TimeSpan.FromSeconds(60), ct);

		// 4. Verify results
		await VerifyUserStates(projectionName, scenarios, ct);
	}

	[Test]
	[Timeout(180_000)]
	public async Task v1_happy_path_events_appended_before_projection(CancellationToken ct) {
		var testId = Guid.NewGuid().ToString("N")[..8];
		var category = $"u1b{testId}";
		var projectionName = $"user-login-v1-after-{testId}";
		var scenarios = GenerateUserScenarios(category, 100);

		// 1. Append events first
		await AppendUserEvents(scenarios, ct);

		// 2. Wait for $by_category indexing
		await Task.Delay(5000, ct);

		// 3. Create projection
		await CreateProjection(projectionName, GetProjectionSource(category), engineVersion: 1, ct);

		// 4. Wait for projection to catch up
		await WaitForProjectionToProcessAllEvents(projectionName, TotalEventCount(scenarios), TimeSpan.FromSeconds(60), ct);

		// 5. Verify results
		await VerifyUserStates(projectionName, scenarios, ct);
	}

	[Test]
	[Timeout(180_000)]
	public async Task v1_recovery_projection_stops_and_resumes(CancellationToken ct) {
		var testId = Guid.NewGuid().ToString("N")[..8];
		var category = $"u1c{testId}";
		var projectionName = $"user-login-v1-recovery-{testId}";

		var firstBatch = GenerateUserScenarios(category, 50, seed: 1);
		var secondBatch = GenerateUserScenarios(category, 50, seed: 2);
		// Offset user IDs for second batch to avoid collisions
		secondBatch = secondBatch.Select(s => s with { UserId = s.UserId.Replace("user0", "user1") }).ToList();
		var allScenarios = firstBatch.Concat(secondBatch).ToList();

		// 1. Create projection
		await CreateProjection(projectionName, GetProjectionSource(category), engineVersion: 1, ct);
		await WaitForProjectionStatus(projectionName, "Running", TimeSpan.FromSeconds(30), ct);

		// 2. Append first batch
		await AppendUserEvents(firstBatch, ct);
		await Task.Delay(5000, ct);
		await WaitForProjectionToProcessAllEvents(projectionName, TotalEventCount(firstBatch), TimeSpan.FromSeconds(60), ct);

		// 3. Stop projection
		await DisableProjection(projectionName, ct);
		await WaitForProjectionStatus(projectionName, "Stopped", TimeSpan.FromSeconds(30), ct);

		// 4. Append second batch while stopped
		await AppendUserEvents(secondBatch, ct);
		await Task.Delay(2000, ct);

		// 5. Resume projection — EventsProcessedAfterRestart resets on enable,
		//    so only expect the second batch event count
		await EnableProjection(projectionName, ct);
		await WaitForProjectionToProcessAllEvents(projectionName, TotalEventCount(secondBatch), TimeSpan.FromSeconds(60), ct);

		// 6. Verify ALL users
		await VerifyUserStates(projectionName, allScenarios, ct);
	}

	#endregion

	#region Tests — V2 Engine

	[Test]
	[Timeout(180_000)]
	public async Task v2_happy_path_projection_created_before_events(CancellationToken ct) {
		var testId = Guid.NewGuid().ToString("N")[..8];
		var category = $"u2a{testId}";
		var projectionName = $"user-login-v2-before-{testId}";
		var scenarios = GenerateUserScenarios(category, 100);

		// 1. Create projection first (engine version 2)
		await CreateProjection(projectionName, GetProjectionSource(category), engineVersion: 2, ct);
		await WaitForProjectionStatus(projectionName, "Running", TimeSpan.FromSeconds(30), ct);

		// 2. Append events
		await AppendUserEvents(scenarios, ct);

		// 3. Wait for projection to catch up
		await Task.Delay(5000, ct);
		await WaitForProjectionToProcessAllEvents(projectionName, TotalEventCount(scenarios), TimeSpan.FromSeconds(60), ct);

		// 4. Verify results
		await VerifyUserStates(projectionName, scenarios, ct);
	}

	[Test]
	[Timeout(180_000)]
	public async Task v2_happy_path_events_appended_before_projection(CancellationToken ct) {
		var testId = Guid.NewGuid().ToString("N")[..8];
		var category = $"u2b{testId}";
		var projectionName = $"user-login-v2-after-{testId}";
		var scenarios = GenerateUserScenarios(category, 100);

		// 1. Append events first
		await AppendUserEvents(scenarios, ct);

		// 2. Wait for $by_category indexing
		await Task.Delay(5000, ct);

		// 3. Create projection (engine version 2)
		await CreateProjection(projectionName, GetProjectionSource(category), engineVersion: 2, ct);

		// 4. Wait for projection to catch up
		await WaitForProjectionToProcessAllEvents(projectionName, TotalEventCount(scenarios), TimeSpan.FromSeconds(60), ct);

		// 5. Verify results
		await VerifyUserStates(projectionName, scenarios, ct);
	}

	[Test]
	[Timeout(180_000)]
	public async Task v2_recovery_projection_stops_and_resumes(CancellationToken ct) {
		var testId = Guid.NewGuid().ToString("N")[..8];
		var category = $"u2c{testId}";
		var projectionName = $"user-login-v2-recovery-{testId}";

		var firstBatch = GenerateUserScenarios(category, 50, seed: 1);
		var secondBatch = GenerateUserScenarios(category, 50, seed: 2);
		secondBatch = secondBatch.Select(s => s with { UserId = s.UserId.Replace("user0", "user1") }).ToList();
		var allScenarios = firstBatch.Concat(secondBatch).ToList();

		// 1. Create projection (engine version 2)
		await CreateProjection(projectionName, GetProjectionSource(category), engineVersion: 2, ct);
		await WaitForProjectionStatus(projectionName, "Running", TimeSpan.FromSeconds(30), ct);

		// 2. Append first batch
		await AppendUserEvents(firstBatch, ct);
		await Task.Delay(5000, ct);
		await WaitForProjectionToProcessAllEvents(projectionName, TotalEventCount(firstBatch), TimeSpan.FromSeconds(60), ct);

		// 3. Stop projection
		await DisableProjection(projectionName, ct);
		await WaitForProjectionStatus(projectionName, "Stopped", TimeSpan.FromSeconds(30), ct);

		// 4. Append second batch while stopped
		await AppendUserEvents(secondBatch, ct);
		await Task.Delay(2000, ct);

		// 5. Resume projection — EventsProcessedAfterRestart resets on enable,
		//    so only expect the second batch event count
		await EnableProjection(projectionName, ct);
		await WaitForProjectionToProcessAllEvents(projectionName, TotalEventCount(secondBatch), TimeSpan.FromSeconds(60), ct);

		// 6. Verify ALL users
		await VerifyUserStates(projectionName, allScenarios, ct);
	}

	#endregion

	#region Tests — V1 to V2 Migration

	/// <summary>
	/// Feasibility test: projection created as V1, processes first batch, then migrated to V2
	/// engine by deleting and re-creating with engine version 2. The V2 engine reads the
	/// existing checkpoint stream written by V1 and resumes from that position.
	/// </summary>
	[Test]
	[Timeout(180_000)]
	public async Task v1_to_v2_migration_projection_resumes_after_engine_upgrade(CancellationToken ct) {
		var testId = Guid.NewGuid().ToString("N")[..8];
		var category = $"mig{testId}";
		var projectionName = $"user-login-migration-{testId}";

		var firstBatch = GenerateUserScenarios(category, 50, seed: 1);
		var secondBatch = GenerateUserScenarios(category, 50, seed: 2);
		secondBatch = secondBatch.Select(s => s with { UserId = s.UserId.Replace("user0", "user1") }).ToList();
		var allScenarios = firstBatch.Concat(secondBatch).ToList();

		// 1. Create V1 projection
		await CreateProjection(projectionName, GetProjectionSource(category), engineVersion: 1, ct);
		await WaitForProjectionStatus(projectionName, "Running", TimeSpan.FromSeconds(30), ct);

		// 2. Append first batch and wait for processing
		await AppendUserEvents(firstBatch, ct);
		await Task.Delay(5000, ct);
		await WaitForProjectionToProcessAllEvents(projectionName, TotalEventCount(firstBatch), TimeSpan.FromSeconds(60), ct);

		// 3. Stop projection (forces checkpoint + result writes)
		await DisableProjection(projectionName, ct);
		await WaitForProjectionStatus(projectionName, "Stopped", TimeSpan.FromSeconds(30), ct);
		await Task.Delay(2000, ct);

		// 4. Delete the V1 projection but keep checkpoint and state streams.
		//    The V2 engine will read the existing checkpoint stream on startup.
		await DeleteProjection(projectionName, deleteCheckpointStream: false, deleteStateStream: false, ct);
		await Task.Delay(2000, ct);

		// 5. Append second batch while no projection exists
		await AppendUserEvents(secondBatch, ct);
		await Task.Delay(2000, ct);

		// 6. Re-create projection with V2 engine — reads V1 checkpoint, resumes from there
		await CreateProjection(projectionName, GetProjectionSource(category), engineVersion: 2, ct);
		await WaitForProjectionStatus(projectionName, "Running", TimeSpan.FromSeconds(30), ct);
		await WaitForProjectionToProcessAllEvents(projectionName, TotalEventCount(secondBatch), TimeSpan.FromSeconds(60), ct);

		// 7. Verify ALL users (first batch from V1 + second batch from V2)
		await VerifyUserStates(projectionName, allScenarios, ct);
	}

	async Task DeleteProjection(string name, bool deleteCheckpointStream, bool deleteStateStream, CancellationToken ct) {
		await Fixture.ProjectionsClient.DeleteAsync(new DeleteReq {
			Options = new DeleteReq.Types.Options {
				Name = name,
				DeleteCheckpointStream = deleteCheckpointStream,
				DeleteStateStream = deleteStateStream,
				DeleteEmittedStreams = false
			}
		}, cancellationToken: ct);
	}

	#endregion
}
