// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using EventStore.Core.Authorization;
using EventStore.Core.Authorization.AuthorizationPolicies;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Services.Transport.Common;
using EventStore.Core.Services.Transport.Enumerators;
using Serilog;

namespace EventStore.Auth.StreamPolicyPlugin;

public delegate bool TryParsePolicy(string eventType, ReadOnlySpan<byte> eventData, out ReadOnlyPolicy policy);

// TODO: use the IClient interface instead of enumerators to read streams
public abstract class StreamBasedPolicySelector : IPolicySelector {
	private readonly IPublisher _publisher;
	private readonly string _stream;
	private readonly ClaimsPrincipal _user;
	private readonly TryParsePolicy _tryParsePolicy;

	private ReadOnlyPolicy _currentPolicy;

	private static readonly ILogger Logger = Log.ForContext<StreamBasedPolicySelector>();

	protected StreamBasedPolicySelector(
		IPublisher publisher,
		string stream,
		ClaimsPrincipal user,
		TryParsePolicy tryParsePolicy,
		ReadOnlyPolicy notReadyPolicy,
		CancellationToken ct) {
		_publisher = publisher;
		_stream = stream;
		_user = user;
		_tryParsePolicy = tryParsePolicy;

		_currentPolicy = notReadyPolicy;
		Task.Factory.StartNew(() => StartAsync(ct));
	}

	private async Task StartAsync(CancellationToken ct) {
		do {
			try {
				var checkpoint = await TryGetSubscriptionCheckpoint(ct);
				await StartSubscription(checkpoint, ct);
				return;
			} catch (ReadResponseException.NotHandled.ServerNotReady) {
				Logger.Verbose("Subscription to {policyStream}: server is not ready, retrying...", _stream);
				await Task.Delay(TimeSpan.FromSeconds(3), ct);
			} catch (ReadResponseException.NotHandled.ServerBusy) {
				Logger.Verbose("Subscription to {policyStream}: server is too busy, retrying...", _stream);
				await Task.Delay(TimeSpan.FromSeconds(3), ct);
			} catch (OperationCanceledException) {
				// ignore
			} catch (Exception exc) {
				Logger.Fatal(exc, "Fatal error in subscription to {policyStream}", _stream);
				return;
			}
		} while (true);
	}

	private async Task StartSubscription(StreamRevision? checkpoint, CancellationToken ct) {
		Logger.Information("Subscribing to {policyStream} at {checkpoint}", _stream, checkpoint);
		await using var sub = new Enumerator.StreamSubscription<string>(
			bus: _publisher,
			expiryStrategy: new DefaultExpiryStrategy(),
			streamName: _stream,
			resolveLinks: false,
			user: _user,
			checkpoint: checkpoint,
			requiresLeader: false,
			cancellationToken: ct);

		while (await sub.MoveNextAsync()) {
			var response = sub.Current;
			switch (response) {
				case ReadResponse.EventReceived evt:
					Logger.Information("New policy found in {policyStream} stream ({eventNumber})", _stream,
						evt.Event.OriginalEventNumber);
					TryApplyPolicy(evt.Event);
					break;
			}
		}
	}

	private bool TryApplyPolicy(ResolvedEvent evt) {
		if (_tryParsePolicy(evt.Event.EventType, evt.Event.Data.Span, out var policy)) {
			_currentPolicy = policy;
			Logger.Information("Successfully applied policy");
			return true;
		}
		Logger.Error("Could not parse policy");
		return false;
	}

	private async Task<StreamRevision?> TryGetSubscriptionCheckpoint(CancellationToken ct) {
		Logger.Verbose("Determining subscription checkpoint for stream {policyStream}", _stream);
		await using var enumerator = new Enumerator.ReadStreamBackwards(
			bus: _publisher,
			streamName: _stream,
			startRevision: StreamRevision.End,
			maxCount: ulong.MaxValue,
			resolveLinks: false,
			user: _user,
			requiresLeader: false,
			deadline: DateTime.MaxValue,
			compatibility: 0,
			cancellationToken: ct);

		StreamRevision? checkpoint = null;
		while (await enumerator.MoveNextAsync()) {
			var response = enumerator.Current;
			switch (response) {
				case ReadResponse.StreamNotFound:
					Log.Information("{policyStream} stream not found", _stream);
					return null;
				case ReadResponse.EventReceived evt:
					Logger.Information("Existing policy found in {policyStream} stream ({eventNumber})", _stream, evt.Event.OriginalEventNumber);

					// assign the checkpoint only for the first event we find as we want to subscribe from there
					checkpoint ??= StreamRevision.FromInt64(evt.Event.OriginalEventNumber);

					if (!TryApplyPolicy(evt.Event)) {
						// we haven't been able to apply the policy we found since it's invalid.
						// let's continue reading events until we find a valid one, otherwise
						// we'll default to the not ready policy
						continue;
					}

					return checkpoint;
			}
		}
		// all the policies are invalid
		// we subscribe anyway and remain on the not ready policy
		Logger.Warning("No valid policy found in {policyStream} stream", _stream);
		return checkpoint;
	}

	public ReadOnlyPolicy Select() => _currentPolicy;
}

