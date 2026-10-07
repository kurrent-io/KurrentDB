// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using KurrentDB.Common.Exceptions;
using KurrentDB.Common.Options;
using KurrentDB.Core.Services.Monitoring;

namespace KurrentDB.Embedded.Tests;

/// <summary>
/// The options an embedded database insists on are expressed as configuration keys, which nothing checks
/// for us: a key under the wrong section binds to nothing, and the engine quietly keeps its own default.
/// These assert that what we ask for is what the node ends up with.
/// </summary>
/// <remarks>
/// No node is started here. The constructor settles the configuration and nothing more, so these are
/// cheap.
/// </remarks>
public class EmbeddedKurrentDBOptionsTests {
	[Test]
	public async Task settles_the_options_the_embedded_node_depends_on() {
		var dataDirectory = TestPaths.NewDataDirectory();

		using var _ = new Cleanup(dataDirectory);
		await using var db = new EmbeddedKurrentDB(TestPaths.Options(dataDirectory));

		var serverOptions = db.ServerOptions;

		// the socket is the way in, and it is created in the database directory
		serverOptions.Interface.EnableUnixSocket.ShouldBeTrue();
		serverOptions.Database.Db.ShouldBe(db.DataDirectory);

		// one node, so no replication listener and no seed resolution
		serverOptions.Cluster.ClusterSize.ShouldBe(1);
		serverOptions.Cluster.DiscoverViaDns.ShouldBeFalse();

		// part of someone else's process: it does not write to the host's log, nor to it on a timer
		serverOptions.Logging.DisableLogFile.ShouldBeTrue();
		serverOptions.Database.StatsStorage.ShouldBe(StatsStorage.None);
	}

	[Test]
	public async Task lets_settings_override_what_it_chose() {
		var dataDirectory = TestPaths.NewDataDirectory();

		using var _ = new Cleanup(dataDirectory);
		await using var db = new EmbeddedKurrentDB(TestPaths.Options(dataDirectory) with {
			Settings = new Dictionary<string, string?> {
				["KurrentDB:NodePort"] = "21139"
			}
		});

		db.ServerOptions.Interface.NodePort.ShouldBe(21139);
	}

	[Test]
	public async Task reports_telemetry_unless_told_not_to() {
		var dataDirectory = TestPaths.NewDataDirectory();

		using var _ = new Cleanup(dataDirectory);
		await using var db = new EmbeddedKurrentDB(TestPaths.Options(dataDirectory));

		// the same default as the server: embedding is a deployment shape, not a privacy policy
		db.ServerOptions.Application.TelemetryOptout.ShouldBeFalse();
	}

	[Test]
	public async Task opts_out_of_telemetry_when_asked() {
		var dataDirectory = TestPaths.NewDataDirectory();

		using var _ = new Cleanup(dataDirectory);
		await using var db = new EmbeddedKurrentDB(TestPaths.Options(dataDirectory) with {
			TelemetryOptout = true
		});

		// the option is the only way to reach this setting, so it had better work
		db.ServerOptions.Application.TelemetryOptout.ShouldBeTrue();
	}

	[Test]
	public async Task refuses_a_setting_the_server_only_takes_from_the_environment() {
		var dataDirectory = TestPaths.NewDataDirectory();

		using var _ = new Cleanup(dataDirectory);

		// the library sets TelemetryOptout itself, through the defaults source, which is exempt. A caller
		// supplying one is not, exactly as it would not be from a configuration file — which is why the
		// option exists, and is covered by the two tests above.
		var options = TestPaths.Options(dataDirectory) with {
			Settings = new Dictionary<string, string?> {
				["KurrentDB:TelemetryOptout"] = bool.FalseString
			}
		};

		Should.Throw<InvalidConfigurationException>(() => new EmbeddedKurrentDB(options));

		await Task.CompletedTask;
	}

	[Test]
	public async Task reports_a_mistyped_setting_rather_than_ignoring_it() {
		var dataDirectory = TestPaths.NewDataDirectory();

		using var _ = new Cleanup(dataDirectory);
		await using var db = new EmbeddedKurrentDB(TestPaths.Options(dataDirectory) with {
			Settings = new Dictionary<string, string?> {
				["KurrentDB:NodePrt"] = "21139"
			}
		});

		// AllowUnknownOptions is left at the server's default, so StartAsync refuses this rather than
		// leaving the caller with a database that quietly ignored what they asked for
		db.ServerOptions.Application.AllowUnknownOptions.ShouldBeFalse();
		db.ServerOptions.UnknownOptionsDetected.ShouldBeTrue();
	}

	[Test]
	public async Task does_not_report_a_nested_setting_because_plugins_use_them() {
		var dataDirectory = TestPaths.NewDataDirectory();

		using var _ = new Cleanup(dataDirectory);
		await using var db = new EmbeddedKurrentDB(TestPaths.Options(dataDirectory) with {
			Settings = new Dictionary<string, string?> {
				["KurrentDB:Licensing:LicenseKey"] = "a-licence-key"
			}
		});

		// the flip side of the above, and the reason a caller who writes KurrentDB:Database:ChunkSize gets
		// neither the setting nor a complaint
		db.ServerOptions.UnknownOptionsDetected.ShouldBeFalse();
	}

	sealed class Cleanup(string dataDirectory) : IDisposable {
		public void Dispose() => TestPaths.Delete(dataDirectory);
	}
}
