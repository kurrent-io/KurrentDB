// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Authentication;
using EventStore.Core.Authentication.InternalAuthentication;
using EventStore.Core.Authorization;
using EventStore.Core.Authorization.AuthorizationPolicies;
using EventStore.Core.Certificates;
using EventStore.Core.LogAbstraction;
using EventStore.Core.Tests;
using EventStore.Core.Tests.Services.Transport.Tcp;
using EventStore.Core.Transforms.Identity;
using EventStore.Plugins.Transforms;
using NUnit.Framework;

namespace EventStore.Core.XUnit.Tests.Configuration.ClusterNodeOptionsTests;

[TestFixture]
public abstract class SingleNodeScenario<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
	protected ClusterVNode _node;
	protected ClusterVNodeOptions _options;
	private ILogFormatAbstractorFactory<TStreamId> _logFormatFactory;
	private readonly bool _disableMemoryOptimization;

	public SingleNodeScenario (bool disableMemoryOptimization=false) {
		_disableMemoryOptimization = disableMemoryOptimization;
	}

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();
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

		_node.Db.TransformManager.LoadTransforms([new IdentityDbTransform()]);
		_node.Db.TransformManager.SetActiveTransform(TransformType.Identity);

		await _node.StartAsync(waitUntilReady: true, CancellationToken.None);
	}

	[OneTimeTearDown]
	public override async Task TestFixtureTearDown() {
		await (_node?.StopAsync() ?? Task.CompletedTask);
		await base.TestFixtureTearDown();
	}

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
	public virtual async Task TestFixtureSetUp() {
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

		_node.Db.TransformManager.LoadTransforms([new IdentityDbTransform()]);
		_node.Db.TransformManager.SetActiveTransform(TransformType.Identity);

		// we do not wait until ready because in some cases we will never become ready.
		// becoming ready involves writing the admin user, which we will never in the test
		// cases that a node that is configured to be part of a cluster.
		await _node.StartAsync(waitUntilReady: false, CancellationToken.None);
	}

	[OneTimeTearDown]
	public virtual Task TestFixtureTearDown() => _node?.StopAsync() ?? Task.CompletedTask;

	protected abstract ClusterVNodeOptions WithOptions(ClusterVNodeOptions options);
}
