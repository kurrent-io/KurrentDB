// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable
using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Common.Exceptions;
using EventStore.Core.Bus;
using EventStore.Core.Services;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Services.Transport.Common;
using EventStore.Core.Services.Transport.Enumerators;
using EventStore.Core.Services.UserManagement;
using Serilog;
using ResolvedEvent = EventStore.Core.Data.ResolvedEvent;

namespace EventStore.Core.Authorization.AuthorizationPolicies;

public class StreamBasedAuthorizationPolicyRegistry :
	IAuthorizationPolicyRegistry {
	private readonly string _stream = SystemStreams.AuthorizationPolicyRegistryStream;
	private readonly ILogger _logger = Log.ForContext<StreamBasedAuthorizationPolicyRegistry>();
	private readonly IPublisher _publisher;

	private readonly FallbackStreamAccessPolicySelector _fallbackStreamAccessPolicySelector = new ();
	private readonly IPolicySelector _legacyPolicySelector;
	private readonly IPolicySelectorFactory[] _pluginSelectorFactories;
	private readonly AuthorizationPolicySettings _defaultSettings;
	public event EventHandler<PolicyChangedEventArgs>? PolicyChanged;

	private CancellationTokenSource? _cts;
	private IPolicySelector[] _effectivePolicySelectors = [];

	private static readonly JsonSerializerOptions SerializeOptions = new() {
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	public class PolicyChangedEventArgs : EventArgs {
		public ReadOnlyPolicy[] EffectivePolicies { get; }

		public PolicyChangedEventArgs(ReadOnlyPolicy[] effectivePolicies) {
			EffectivePolicies = effectivePolicies;
		}
	}

	public StreamBasedAuthorizationPolicyRegistry(IPublisher publisher, IPolicySelector legacyPolicySelector, IPolicySelectorFactory[] pluginSelectorFactories, AuthorizationPolicySettings defaultSettings) {
		_publisher = publisher;
		_legacyPolicySelector = legacyPolicySelector;
		_pluginSelectorFactories = pluginSelectorFactories;
		_defaultSettings = defaultSettings;
	}

	public ReadOnlyPolicy[] EffectivePolicies {
		get {
			return _effectivePolicySelectors.Length != 0
				? _effectivePolicySelectors.Select(x => x.Select()).ToArray()
				: [_fallbackStreamAccessPolicySelector.Select(), _legacyPolicySelector.Select()];
		}
	}

	public async Task Start() {
		_cts = new CancellationTokenSource();
		do {
			try {
				var checkpoint = await LoadSettings(_cts.Token);
				_ = StartSubscription(checkpoint, _cts.Token);
				return;
			} catch (ReadResponseException.NotHandled.ServerNotReady) {
				_logger.Verbose("Subscription to {settingsStream}: server is not ready, retrying...", _stream);
				await Task.Delay(TimeSpan.FromSeconds(3), _cts.Token);
			} catch (ReadResponseException.NotHandled.ServerBusy) {
				_logger.Verbose("Subscription to {settingsStream}: server is too busy, retrying...", _stream);
				await Task.Delay(TimeSpan.FromSeconds(3), _cts.Token);
			} catch (ReadResponseException.Timeout) {
				_logger.Verbose("Subscription to {settingsStream}: timeout, retrying...", _stream);
				await Task.Delay(TimeSpan.FromSeconds(3), _cts.Token);
			} catch (Exception exc) {
				_logger.Fatal(exc, "Fatal error starting the subscription to {settingsStream}", _stream);
				throw new ApplicationInitializationException($"Fatal error starting the subscription to {_stream}");
			}
		} while (true);
	}

	public async Task Stop() {
		foreach (var factory in _pluginSelectorFactories) {
			_logger.Information("Stopping policy selector factory {name}", factory.CommandLineName);
			await factory.Disable();
		}
		if (_cts is not null) await _cts.CancelAsync();
	}

	private async Task StartSubscription(ulong? checkpoint, CancellationToken ct) {
		var start = checkpoint.HasValue
			? new StreamRevision(checkpoint.GetValueOrDefault())
			: (StreamRevision?)null;
		await using var sub = new Enumerator.StreamSubscription<string>(
			bus: _publisher,
			expiryStrategy: new DefaultExpiryStrategy(),
			streamName: _stream,
			resolveLinks: false,
			user: SystemAccounts.System,
			checkpoint: start,
			requiresLeader: false,
			cancellationToken: ct);

		while (await sub.MoveNextAsync()) {
			var response = sub.Current;
			switch (response) {
				case ReadResponse.EventReceived evnt:
					_logger.Information("New Authorization Policy Settings event received");
					var (success, settings) = TryParseAuthorizationPolicySettings(evnt.Event);
					if (!success) {
						_logger.Warning("New authorization settings could not be applied. Settings were not updated.");
					} else {
						await TryApplyAuthorizationPolicySettings(settings);
					}
					break;
			}
		}
	}

	private async ValueTask ApplyFallbackPolicySelector() {
		_logger.Debug("Applying fallback stream access policy.");
		_effectivePolicySelectors = [];
		foreach (var factory in _pluginSelectorFactories) {
			await factory.Disable();
		}

		PolicyChanged?.Invoke(this, new PolicyChangedEventArgs(EffectivePolicies));
	}

	private async ValueTask ApplyLegacyPolicySelector() {
		_logger.Debug("Applying ACL stream access policy.");
		_effectivePolicySelectors = [_legacyPolicySelector];
		foreach (var factory in _pluginSelectorFactories) {
			await factory.Disable();
		}

		PolicyChanged?.Invoke(this, new PolicyChangedEventArgs(EffectivePolicies));
	}

	private (bool success, AuthorizationPolicySettings settings) TryParseAuthorizationPolicySettings(ResolvedEvent evnt) {
		if (evnt.Event.EventType != SystemEventTypes.AuthorizationPolicyChanged) {
			_logger.Error(
				"Invalid authorization policy settings event. Expected event type {expectedType} but got {actualType}",
				SystemEventTypes.AuthorizationPolicyChanged, evnt.Event.EventType);
			return (false, new AuthorizationPolicySettings());
		}

		try {
			var settings = JsonSerializer.Deserialize<AuthorizationPolicySettings>(evnt.Event.Data.Span, SerializeOptions);
			if (settings is not null) return (true, settings);
			_logger.Error("Could not parse authorization policy settings");
		} catch (Exception ex) {
			_logger.Error(ex, "Could not parse authorization policy settings");
		}
		return (false, new AuthorizationPolicySettings());
	}

	private async ValueTask<bool> TryApplyPluginPolicySelector(IPolicySelectorFactory pluginFactory) {
		_logger.Information("Starting authorization policy factory {factory}", pluginFactory.CommandLineName);
		if (!await pluginFactory.Enable()) {
			_logger.Error("Failed to enable policy selector plugin {pluginName}. " +
			              "Authorization settings will not be applied", pluginFactory.CommandLineName);
			return false;
		}

		var selector = pluginFactory.Create(_publisher);
		_effectivePolicySelectors = [selector, _legacyPolicySelector];
		foreach (var otherFactory in _pluginSelectorFactories) {
			try {
				if (otherFactory.CommandLineName != pluginFactory.CommandLineName) {
					await otherFactory.Disable();
				}
			} catch (Exception ex) {
				_logger.Warning(ex, "Failed to disable policy selector plugin {pluginName}", otherFactory.CommandLineName);
			}
		}

		PolicyChanged?.Invoke(this, new PolicyChangedEventArgs(EffectivePolicies));
		return true;
	}

	private async ValueTask<bool> TryApplyAuthorizationPolicySettings(AuthorizationPolicySettings settings) {
		switch (settings.StreamAccessPolicyType) {
			case FallbackStreamAccessPolicySelector.FallbackPolicyName:
				await ApplyFallbackPolicySelector();
				return true;
			case LegacyPolicySelectorFactory.LegacyPolicySelectorName:
				await ApplyLegacyPolicySelector();
				return true;
			default:
				var factory =
					_pluginSelectorFactories.FirstOrDefault(x =>
						x.CommandLineName == settings.StreamAccessPolicyType);
				if (factory is not null)
					return await TryApplyPluginPolicySelector(factory);

				_logger.Error("Could not find policy {commandLineName} in registered authorization policy plugins.",
					settings.StreamAccessPolicyType);
				return false;
		}
	}

	private async ValueTask<ulong?> LoadSettings(CancellationToken ct) {
		ulong? checkpoint = null;
		try {
			await using var read = new Enumerator.ReadStreamBackwards(_publisher, _stream,
				StreamRevision.End, ulong.MaxValue, false, SystemAccounts.System, false, DateTime.MaxValue, 1, ct);
			while (await read.MoveNextAsync()) {
				var readResponse = read.Current;
				switch (readResponse) {
					case ReadResponse.EventReceived evnt:
						checkpoint ??= (ulong)evnt.Event.OriginalEventNumber;
						var (success, settings) = TryParseAuthorizationPolicySettings(evnt.Event);
						if (!success) {
							Log.Error(
								"Could not load authorization policy settings from event {eventNumber}@{eventStream}",
								evnt.Event.OriginalEventNumber, evnt.Event.OriginalStreamId);
						} else {
							if (await TryApplyAuthorizationPolicySettings(settings)) {
								Log.Information("Authorization settings successfully loaded.");
								return checkpoint;
							}

							Log.Error("Could not apply authorization settings.");
						}

						break;
				}
			}
		} catch (ReadResponseException.StreamDeleted) {
			_logger.Warning("Authorization policy settings stream {stream} has been deleted.", _stream);
		} catch (ReadResponseException.StreamNotFound) {
			// ignore
		}

		if (checkpoint is null) {
			// There are no events in the stream.
			// Use the default.
			Log.Information("No existing authorization policy settings were found in {stream}. Using the default", _stream);
			if (await TryApplyAuthorizationPolicySettings(_defaultSettings)) {
				_logger.Verbose("Successfully applied default settings");
				return checkpoint;
			}
		}

		// There are events in the stream, but none of the policy selectors can be enabled.
		// Fall back to restricted access.
		_logger.Warning("Could not load authorization policy settings. Restricting access to admins only.");
		await ApplyFallbackPolicySelector();
		return checkpoint;
	}
}
