// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.ComponentModel.Composition;
using EventStore.Core.Authorization.AuthorizationPolicies;
using EventStore.Core.Bus;
using EventStore.Plugins;
using EventStore.Plugins.Subsystems;
using Serilog;
using ILogger = Serilog.ILogger;

namespace EventStore.Auth.StreamPolicyPlugin;

[Export(typeof(IPolicySelectorFactory))]
public class StreamPolicySelectorFactory : Plugin, IPolicySelectorFactory, ISubsystem {
	public string CommandLineName => Name.Replace("Plugin", "").ToLowerInvariant();
	private static readonly ILogger Logger = Log.ForContext<StreamPolicySelector>();
	private IPolicySelector? _policySelector;
	private bool _enabled;
	private bool _licensed = true;
	private CancellationTokenSource _cts = new();

	public StreamPolicySelectorFactory()
		: base(
			name: "StreamPolicyPlugin",
			requiredEntitlements: ["STREAM_POLICY_AUTHORIZATION"]) {
	}

	protected override void OnLicenseException(Exception ex, Action<Exception> shutdown) {
		Logger.Information("Stream Policies plugin is not licensed, stream policy authorization cannot be enabled.");
		_licensed = false;
	}

	public bool IsLicensed => _licensed; // temporary

	public IPolicySelector Create(IPublisher publisher) {
		if (!_enabled) {
			throw new InvalidOperationException(
				$"Cannot create a {nameof(StreamPolicySelector)} while the Stream Policies plugin is disabled.");
		}
		// Always try to create the default policy
		// This should result in WrongExpectedVersion if the default policy already exists
		var writer = new StreamPolicyWriter(publisher, StreamPolicySelector.PolicyStream,
			StreamPolicySelector.PolicyEventType, StreamPolicySelector.SerializePolicy);
		_ = writer?.WriteDefaultPolicy(StreamPolicySelector.DefaultPolicy, _cts.Token);
		return _policySelector ??= new StreamPolicySelector(publisher, _cts.Token);
	}

	public Task<bool> Enable() {
		if (_enabled) return Task.FromResult(_enabled);

		if (!_licensed) {
			Logger.Error("Stream Policies plugin is not licensed, cannot enable Stream policies");
			_enabled = false;
		} else {
			_enabled = true;
			_cts = new CancellationTokenSource();
		}
		return Task.FromResult(_enabled);
	}

	public async Task Disable() {
		if (_enabled) {
			await _cts.CancelAsync();
			_policySelector = null;
		}
		_enabled = false;
	}

	public Task Start() => Task.CompletedTask;

	public Task Stop() => Task.CompletedTask;
}

