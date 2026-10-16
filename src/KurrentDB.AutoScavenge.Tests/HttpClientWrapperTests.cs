// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Services.Transport.Http.NodeHttpClientFactory;

namespace KurrentDB.AutoScavenge.Tests;

public class HttpClientWrapperTests {
	[Theory]
	[InlineData(false, false, "https")]
	[InlineData(true,  false, "http")]
	[InlineData(false, true,  "http")]
	[InlineData(true,  true,  "http")]
	public void protocol_reflects_tls_state(bool insecure, bool disableTls, string expectedProtocol) {
		var options = new EventStoreOptions {
			Insecure = insecure,
			DisableTls = disableTls,
		};

		var sut = new HttpClientWrapper(options, new DummyNodeHttpClientFactory());

		Assert.Equal(expectedProtocol, sut.Protocol);
	}

	[Fact]
	public void local_node_client_uses_the_hosts_handler_over_cleartext() {
		var localNodeHttpHandlerFactory = new RecordingLocalNodeHttpHandlerFactory();

		var sut = HttpClientWrapper.ForLocalNode(localNodeHttpHandlerFactory);

		Assert.Equal("http", sut.Protocol);
		Assert.Equal(1, localNodeHttpHandlerFactory.Created);
	}

	private class RecordingLocalNodeHttpHandlerFactory : ILocalNodeHttpHandlerFactory {
		public int Created { get; private set; }

		public HttpMessageHandler CreateHandler() {
			Created++;
			return new HttpClientHandler();
		}
	}
}
