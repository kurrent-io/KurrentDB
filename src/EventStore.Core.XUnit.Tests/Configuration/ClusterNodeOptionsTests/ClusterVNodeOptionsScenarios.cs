// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using EventStore.Core.Authentication;
using EventStore.Core.Authentication.InternalAuthentication;
using EventStore.Core.Authorization;
using EventStore.Core.Authorization.AuthorizationPolicies;
using EventStore.Core.Certificates;
using EventStore.Core.LogAbstraction;
using EventStore.Core.Tests;
using EventStore.Core.Tests.Services.Transport.Tcp;
using NUnit.Framework;

namespace EventStore.Core.XUnit.Tests.Configuration.ClusterNodeOptionsTests;

[TestFixture]
public abstract class SingleNodeScenario<TLogFormat, TStreamId> {
	protected ClusterVNode _node;
	protected ClusterVNodeOptions _options;
	private ILogFormatAbstractorFactory<TStreamId> _logFormatFactory;
	private readonly bool _disableMemoryOptimization;

	public SingleNodeScenario(bool disableMemoryOptimization = false) {
		_disableMemoryOptimization = disableMemoryOptimization;
	}

	[OneTimeSetUp]
	public virtual void TestFixtureSetUp() {
		_logFormatFactory = LogFormatHelper<TLogFormat, TStreamId>.LogFormatFactory;

		var options = _disableMemoryOptimization
			? new ClusterVNodeOptions()
			: new ClusterVNodeOptions().ReduceMemoryUsageForTests();

		_options = WithOptions(options
			.RunInMemory()
			.Secure(new X509Certificate2Collection(ssl_connections.GetRootCertificate()),
				ssl_connections.GetServerCertificate()));
		_node = new ClusterVNode<TStreamId>(_options, _logFormatFactory,
			new AuthenticationProviderFactory(c =>
				new InternalAuthenticationProviderFactory(c, _options.DefaultUser)),
			new AuthorizationProviderFactory(c => new InternalAuthorizationProviderFactory(
				new StaticAuthorizationPolicyRegistry([new LegacyPolicySelectorFactory(
					options.Application.AllowAnonymousEndpointAccess,
					options.Application.AllowAnonymousStreamAccess,
					options.Application.OverrideAnonymousEndpointAccessForGossip).Create(c.MainQueue)]))),
			certificateProvider: new OptionsCertificateProvider());
		_node.Start();
	}

	[OneTimeTearDown]
	public virtual Task TestFixtureTearDown() => _node?.StopAsync() ?? Task.CompletedTask;

	protected abstract ClusterVNodeOptions WithOptions(ClusterVNodeOptions options);

}

[TestFixture, Category("LongRunning")]
public abstract class ClusterMemberScenario<TLogFormat, TStreamId> {
	protected ClusterVNode _node;
	protected int _clusterSize = 3;
	protected int _quorumSize;
	protected ClusterVNodeOptions _options;
	protected ILogFormatAbstractorFactory<TStreamId> _logFormatFactory;

	[OneTimeSetUp]
	public virtual void TestFixtureSetUp() {
		_logFormatFactory = LogFormatHelper<TLogFormat, TStreamId>.LogFormatFactory;
		_quorumSize = _clusterSize / 2 + 1;

		_options = WithOptions(new ClusterVNodeOptions()
			.ReduceMemoryUsageForTests()
			.InCluster(_clusterSize)
			.RunInMemory()
			.Secure(new X509Certificate2Collection(ssl_connections.GetRootCertificate()),
				ssl_connections.GetServerCertificate()));
		_node = new ClusterVNode<TStreamId>(_options, _logFormatFactory,
			new AuthenticationProviderFactory(_ =>
				new InternalAuthenticationProviderFactory(_, _options.DefaultUser)),
			new AuthorizationProviderFactory(c => new InternalAuthorizationProviderFactory(
				new StaticAuthorizationPolicyRegistry([new LegacyPolicySelectorFactory(
					_options.Application.AllowAnonymousEndpointAccess,
					_options.Application.AllowAnonymousStreamAccess,
					_options.Application.OverrideAnonymousEndpointAccessForGossip).Create(c.MainQueue)]))),
			certificateProvider: new OptionsCertificateProvider());
		_node.Start();
	}

	[OneTimeTearDown]
	public virtual Task TestFixtureTearDown() => _node?.StopAsync() ?? Task.CompletedTask;

	protected abstract ClusterVNodeOptions WithOptions(ClusterVNodeOptions options);
}
