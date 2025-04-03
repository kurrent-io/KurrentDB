// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable
using System.Net;
using EventStore.Core.Configuration.Sources;
using EventStore.Core.Tests;
using EventStore.Core.TransactionLog.Chunks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace EventStore.Core.XUnit.Tests.Configuration.ClusterNodeOptionsTests.when_building;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class with_run_on_disk<TLogFormat, TStreamId> : SingleNodeScenario<TLogFormat, TStreamId> {
	protected override ClusterVNodeOptions WithOptions(ClusterVNodeOptions options) => options.RunOnDisk(PathName);

	[Test]
	public void should_set_memdb_to_false() {
		Assert.IsFalse(_node.Db.Config.InMemDb);
	}

	[Test]
	public void should_set_the_db_path() {
		Assert.AreEqual(PathName, _node.Db.Config.Path);
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class with_run_in_memory<TLogFormat, TStreamId> : SingleNodeScenario<TLogFormat, TStreamId> {
	protected override ClusterVNodeOptions WithOptions(ClusterVNodeOptions options) =>
		options.RunInMemory();

	[Test]
	public void should_set_memdb_to_true() {
		Assert.IsTrue(_node.Db.Config.InMemDb);
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class with_custom_ip_endpoints<TLogFormat, TStreamId> : SingleNodeScenario<TLogFormat, TStreamId> {
	private readonly IPEndPoint _httpEndPoint = new(IPAddress.Parse("127.0.1.15"), 1113);
	private readonly IPEndPoint _internalTcp = new(IPAddress.Parse("127.0.1.15"), 1114);
	private readonly IPEndPoint _externalTcp = new(IPAddress.Parse("127.0.1.15"), 1115);

	protected override ClusterVNodeOptions WithOptions(ClusterVNodeOptions options) {
		return options
			.WithNodeEndpointOn(_httpEndPoint)
			.WithExternalTcpOn(_externalTcp)
			.WithReplicationEndpointOn(_internalTcp);
	}

	[Test]
	public void should_set_http_endpoint() {
		Assert.AreEqual(_httpEndPoint, _node.NodeInfo.HttpEndPoint);
	}

	[Test]
	public void should_set_internal_tcp_endpoint() {
		Assert.AreEqual(_internalTcp, _node.NodeInfo.InternalSecureTcp);
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class with_custom_chunk_size<TLogFormat, TStreamId> : SingleNodeScenario<TLogFormat, TStreamId> {
	private readonly int _chunkSize = 268435;

	protected override ClusterVNodeOptions WithOptions(ClusterVNodeOptions options) => options with {
		Database = options.Database with {
			ChunkSize = _chunkSize
		}
	};

	[Test]
	public void should_set_chunk_size() {
		Assert.AreEqual(_chunkSize, _node.Db.Config.ChunkSize);
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class with_custom_chunk_cache_size<TLogFormat, TStreamId> : SingleNodeScenario<TLogFormat, TStreamId> {
	private readonly long _chunkCacheSize = 268435712;

	protected override ClusterVNodeOptions WithOptions(ClusterVNodeOptions options) => options with {
		Database = options.Database with {
			ChunksCacheSize = _chunkCacheSize
		}
	};

	[Test]
	public void should_set_max_chunk_cache_size() {
		Assert.AreEqual(_chunkCacheSize, _node.Db.Config.MaxChunksCacheSize);
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	with_custom_number_of_cached_chunks<TLogFormat, TStreamId> : SingleNodeScenario<TLogFormat, TStreamId> {
	private readonly int _cachedChunks = 10;

	protected override ClusterVNodeOptions WithOptions(ClusterVNodeOptions options) => options with {
		Database = options.Database with {
			CachedChunks = _cachedChunks
		}
	};

	[Test]
	public void should_set_max_chunk_size_to_the_size_of_the_number_of_cached_chunks() {
		var chunkSizeResult = _cachedChunks * (long)(_node.Db.Config.ChunkSize + ChunkHeader.Size + ChunkFooter.Size);
		Assert.AreEqual(chunkSizeResult, _node.Db.Config.MaxChunksCacheSize);
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class with_custom_advertise_as<TLogFormat, TStreamId> : SingleNodeScenario<TLogFormat, TStreamId> {
	private readonly IPEndPoint _intTcpEndpoint = new(IPAddress.Parse(InternalIp), 1111);
	private readonly IPEndPoint _extTcpEndpoint = new(IPAddress.Parse(ExternalIp), 1113);
	private readonly IPEndPoint _httpEndpoint = new(IPAddress.Parse(ExternalIp), 1116);
	const string InternalIp = "127.0.1.1";
	const string ExternalIp = "127.0.1.2";


	protected override ClusterVNodeOptions WithOptions(ClusterVNodeOptions options) {
		return options
			.WithNodeEndpointOn(_httpEndpoint)
			.WithExternalTcpOn(_extTcpEndpoint)
			.WithReplicationEndpointOn(_intTcpEndpoint)
			.AdvertiseInternalHostAs(new DnsEndPoint($"{InternalIp}.com", _intTcpEndpoint.Port + 1000))
			.AdvertiseExternalHostAs(new DnsEndPoint($"{ExternalIp}.com", _extTcpEndpoint.Port + 1000))
			.AdvertiseNodeAs(new DnsEndPoint($"{ExternalIp}.com", _httpEndpoint.Port + 1000));
	}

	[Test]
	public void should_set_the_advertise_as_info_to_the_specified() {
		Assert.AreEqual(null, _node.GossipAdvertiseInfo.InternalTcp);
		Assert.AreEqual(null, _node.GossipAdvertiseInfo.ExternalTcp);
		Assert.AreEqual(new DnsEndPoint($"{InternalIp}.com", _intTcpEndpoint.Port + 1000),
			_node.GossipAdvertiseInfo.InternalSecureTcp);
		Assert.AreEqual(new DnsEndPoint($"{ExternalIp}.com", _httpEndpoint.Port + 1000),
			_node.GossipAdvertiseInfo.HttpEndPoint);
		Assert.AreEqual($"{InternalIp}.com", _node.GossipAdvertiseInfo.AdvertiseInternalHostAs);
		Assert.AreEqual($"{ExternalIp}.com", _node.GossipAdvertiseInfo.AdvertiseExternalHostAs);
		Assert.AreEqual(_httpEndpoint.Port + 1000, _node.GossipAdvertiseInfo.AdvertiseHttpPortAs);
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	with_custom_password_for_admin_and_ops_user<TLogFormat, TStreamId> : SingleNodeScenario<TLogFormat, TStreamId> {
	private const string _adminPassword = "Admin";
	private const string _opsPassword = "Ops";

	protected override ClusterVNodeOptions WithOptions(ClusterVNodeOptions options) =>
		options with {
			DefaultUser = new ClusterVNodeOptions.DefaultUserOptions
				{ DefaultAdminPassword = _adminPassword, DefaultOpsPassword = _opsPassword }
		};

	[Test]
	public void should_set_the_custom_admin_and_ops_user_password() {
		Assert.AreEqual(_adminPassword, _options.DefaultUser.DefaultAdminPassword);
		Assert.AreEqual(_opsPassword, _options.DefaultUser.DefaultOpsPassword);
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	with_custom_settings_check_for_environment_only_options<TLogFormat, TStreamId> : SingleNodeScenario<TLogFormat,
	TStreamId> {
	protected override ClusterVNodeOptions WithOptions(ClusterVNodeOptions options) => options with { };

	[Test]
	public void should_return_error_when_default_password_options_pass_through_command_line() {
		var configuration = new ConfigurationBuilder()
			.AddKurrentDefaultValues()
			.AddKurrentCommandLine(
				"--DefaultAdminPassword=Admin#",
				"--DefaultOpsPassword=Ops#")
			.Build();

		var options = ClusterVNodeOptions.FromConfiguration(configuration);

		var result = options.CheckForEnvironmentOnlyOptions();

		result.Should().NotBeNull();
	}

	[Test]
	public void should_return_null_when_default_password_options_pass_through_environment_variables() {
		var prefix = KurrentConfigurationKeys.Prefix.ToUpper();
		var configuration = new ConfigurationBuilder()
			.AddKurrentDefaultValues()
			.AddKurrentEnvironmentVariables(
				($"{prefix}_DEFAULT_ADMIN_PASSWORD", "Admin#"),
				($"{prefix}_DEFAULT_OPS_PASSWORD", "Ops#")
			)
			.AddKurrentCommandLine()
			.Build();

		var options = ClusterVNodeOptions.FromConfiguration(configuration);

		var result = options.CheckForEnvironmentOnlyOptions();

		result.Should().BeNull();
	}

	[Test]
	public void ignores_subsection_arguments() {
		var prefix = KurrentConfigurationKeys.Prefix;
		var configuration = new ConfigurationBuilder()
			// we should be able to stop doing this soon as long as we bind the options automatically
			.AddKurrentDefaultValues()
			.AddKurrentEnvironmentVariables(
				($"{prefix.ToUpper()}__METRICS__X", "xxx"),
				($"{prefix.ToUpper()}__PLUGINS__Y", "yyy")
			)
			.AddKurrentCommandLine(
				$"--{prefix}:Metrics:A aaa " +
				$"--{prefix}:Plugins:B bbb"
			)
			.Build();

		var options = ClusterVNodeOptions.FromConfiguration(configuration);

		options.Unknown.Options.Should().BeEmpty();
	}
}
