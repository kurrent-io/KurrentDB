// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.ClientAPI;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI;

[TestFixture, Category("ClientAPI")]
public class connection_string {
	[Test]
	public void can_set_bool_value_with_string() {
		var settings = ConnectionString.GetConnectionSettings("verboselogging=true");
		Assert.AreEqual(true, settings.VerboseLogging);
	}

	[Test]
	public void can_set_with_spaces() {
		var settings = ConnectionString.GetConnectionSettings("Verbose Logging=true");
		Assert.AreEqual(true, settings.VerboseLogging);
	}


	[Test]
	public void can_set_int() {
		var settings = ConnectionString.GetConnectionSettings("maxretries=55");
		Assert.AreEqual(55, settings.MaxRetries);
	}

	[Test]
	public void can_set_timespan() {
		var settings = ConnectionString.GetConnectionSettings("heartbeattimeout=5555");
		Assert.AreEqual(5555, settings.HeartbeatTimeout.TotalMilliseconds);
	}

	[Test]
	public void can_set_multiple_values() {
		var settings = ConnectionString.GetConnectionSettings("heartbeattimeout=5555;maxretries=55");
		Assert.AreEqual(5555, settings.HeartbeatTimeout.TotalMilliseconds);
		Assert.AreEqual(55, settings.MaxRetries);
	}

	[Test]
	public void can_set_mixed_case() {
		var settings = ConnectionString.GetConnectionSettings("heArtbeAtTimeout=5555");
		Assert.AreEqual(5555, settings.HeartbeatTimeout.TotalMilliseconds);
	}

	[Test]
	public void can_set_gossip_seeds() {
		var settings =
			ConnectionString.GetConnectionSettings(
				"gossipseeds=111.222.222.111:1111,111.222.222.111:1112,111.222.222.111:1113");
		Assert.AreEqual(3, settings.GossipSeeds.Length);
	}

	[Test]
	public void can_set_default_user_credentials() {
		var settings = ConnectionString.GetConnectionSettings("DefaultUserCredentials=foo:bar");
		Assert.AreEqual("foo", settings.DefaultUserCredentials.Username);
		Assert.AreEqual("bar", settings.DefaultUserCredentials.Password);
	}
}
