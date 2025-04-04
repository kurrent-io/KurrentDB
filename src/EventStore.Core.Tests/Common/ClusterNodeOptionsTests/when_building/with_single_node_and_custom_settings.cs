using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using EventStore.Common.Configuration;
using EventStore.Core.Services;
using EventStore.Core.TransactionLog.Chunks;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace EventStore.Core.Tests.Common.ClusterNodeOptionsTests.when_building {
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class with_run_on_disk<TLogFormat, TStreamId> : SingleNodeScenario<TLogFormat, TStreamId> {
		private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"Test-{Guid.NewGuid()}");

		protected override ClusterVNodeOptions WithOptions(ClusterVNodeOptions options) => options.RunOnDisk(_dbPath);

		[Test]
		public void should_set_memdb_to_false() {
			Assert.IsFalse(_node.Db.Config.InMemDb);
		}

		[Test]
		public void should_set_the_db_path() {
			Assert.AreEqual(_dbPath, _node.Db.Config.Path);
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

		protected override ClusterVNodeOptions WithOptions(ClusterVNodeOptions options) => options
			.WithHttpOn(_httpEndPoint)
			.WithExternalSecureTcpOn(_externalTcp)
			.WithInternalSecureTcpOn(_internalTcp);

		[Test]
		public void should_set_http_endpoint() {
			Assert.AreEqual(_httpEndPoint, _node.NodeInfo.HttpEndPoint);
		}

		[Test]
		public void should_set_internal_tcp_endpoint() {
			Assert.AreEqual(_internalTcp, _node.NodeInfo.InternalSecureTcp);
		}

		[Test]
		public void should_set_external_tcp_endpoint() {
			Assert.AreEqual(_externalTcp, _node.NodeInfo.ExternalSecureTcp);
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
	public class with_custom_number_of_cached_chunks<TLogFormat, TStreamId> : SingleNodeScenario<TLogFormat, TStreamId> {
		private readonly int _cachedChunks = 10;

		protected override ClusterVNodeOptions WithOptions(ClusterVNodeOptions options) => options with {
			Database = options.Database with {
				CachedChunks = _cachedChunks
			}
		};

		[Test]
		public void should_set_max_chunk_size_to_the_size_of_the_number_of_cached_chunks() {
			var chunkSizeResult = _cachedChunks * (long)(TFConsts.ChunkSize + ChunkHeader.Size + ChunkFooter.Size);
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


		protected override ClusterVNodeOptions WithOptions(ClusterVNodeOptions options) =>
			options
				.WithHttpOn(_httpEndpoint)
				.WithExternalSecureTcpOn(_extTcpEndpoint)
				.WithInternalSecureTcpOn(_intTcpEndpoint)
				.AdvertiseInternalHostAs(new DnsEndPoint($"{InternalIp}.com", _intTcpEndpoint.Port + 1000))
				.AdvertiseExternalHostAs(new DnsEndPoint($"{ExternalIp}.com", _extTcpEndpoint.Port + 1000))
				.AdvertiseHttpHostAs(new DnsEndPoint($"{ExternalIp}.com", _httpEndpoint.Port + 1000));

		[Test]
		public void should_set_the_advertise_as_info_to_the_specified() {
			Assert.AreEqual(null, _node.GossipAdvertiseInfo.InternalTcp);
			Assert.AreEqual(null, _node.GossipAdvertiseInfo.ExternalTcp);
			Assert.AreEqual(new DnsEndPoint($"{InternalIp}.com", _intTcpEndpoint.Port + 1000),
				_node.GossipAdvertiseInfo.InternalSecureTcp);
			Assert.AreEqual(new DnsEndPoint($"{ExternalIp}.com", _extTcpEndpoint.Port + 1000),
				_node.GossipAdvertiseInfo.ExternalSecureTcp);
			Assert.AreEqual(new DnsEndPoint($"{ExternalIp}.com", _httpEndpoint.Port + 1000),
				_node.GossipAdvertiseInfo.HttpEndPoint);
			Assert.AreEqual($"{InternalIp}.com", _node.GossipAdvertiseInfo.AdvertiseInternalHostAs);
			Assert.AreEqual($"{ExternalIp}.com", _node.GossipAdvertiseInfo.AdvertiseExternalHostAs);
			Assert.AreEqual(_httpEndpoint.Port + 1000, _node.GossipAdvertiseInfo.AdvertiseHttpPortAs);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class with_custom_password_for_admin_and_ops_user<TLogFormat, TStreamId> : SingleNodeScenario<TLogFormat, TStreamId> {
		private const string _adminPassword = "Admin";
		private const string _opsPassword = "Ops";

		protected override ClusterVNodeOptions WithOptions(ClusterVNodeOptions options) =>
			options with {
				DefaultUser = new ClusterVNodeOptions.DefaultUserOptions { DefaultAdminPassword = _adminPassword, DefaultOpsPassword = _opsPassword }
			};

		[Test]
		public void should_set_the_custom_admin_and_ops_user_password() {
			Assert.AreEqual(_adminPassword, _options.DefaultUser.DefaultAdminPassword);
			Assert.AreEqual(_opsPassword, _options.DefaultUser.DefaultOpsPassword);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class with_custom_settings_check_for_environment_only_options<TLogFormat, TStreamId> : SingleNodeScenario<TLogFormat,
			TStreamId> {
		protected override ClusterVNodeOptions WithOptions(ClusterVNodeOptions options) =>
			options with {
			};

		private IConfigurationRoot _configurationRoot;

		[Test]
		public void should_return_error_when_default_password_options_pass_through_command_line() {

			var args = new string[] {
				"--DefaultAdminPassword=Admin#",
				"--DefaultOpsPassword=Ops#"
			};
			_configurationRoot = new ConfigurationBuilder()
				.Add(new DefaultSource(new Dictionary<string, object> {
					[nameof(ClusterVNodeOptions.DefaultUser.DefaultAdminPassword)] = SystemUsers.DefaultAdminPassword,
					[nameof(ClusterVNodeOptions.DefaultUser.DefaultOpsPassword)] = SystemUsers.DefaultOpsPassword
				}))
				.Add(new CommandLineSource(args))
				.Build();

			var clusterVNodeOptions = ClusterVNodeOptions.FromConfiguration(_configurationRoot);

			Assert.NotNull(clusterVNodeOptions.CheckForEnvironmentOnlyOptions());
		}

		[Test]
		public void should_return_null_when_default_password_options_pass_through_environment_variables() {

			var args = Array.Empty<string>();
			IDictionary environmentVariables = new Dictionary<string, string>();
			environmentVariables.Add("EVENTSTORE_DEFAULT_ADMIN_PASSWORD", "Admin#");
			environmentVariables.Add("EVENTSTORE_DEFAULT_OPS_PASSWORD", "Ops#");

			_configurationRoot = new ConfigurationBuilder()
				.Add(new DefaultSource(new Dictionary<string, object> {
					[nameof(ClusterVNodeOptions.DefaultUser.DefaultAdminPassword)] = SystemUsers.DefaultAdminPassword,
					[nameof(ClusterVNodeOptions.DefaultUser.DefaultOpsPassword)] = SystemUsers.DefaultOpsPassword
				}))
				.Add(new CommandLineSource(args))
				.Add(new EnvironmentVariablesSource(environmentVariables))
				.Build();

			var clusterVNodeOptions = ClusterVNodeOptions.FromConfiguration(_configurationRoot);

			Assert.Null(clusterVNodeOptions.CheckForEnvironmentOnlyOptions());
		}
	}
}
