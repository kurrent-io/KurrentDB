// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Plugins.Authentication;

namespace EventStore.Core.Services.Transport.Http.Controllers;

public class InfoControllerBuilder {
	private ClusterVNodeOptions _options;
	private IDictionary<string, bool> _features;
	private IAuthenticationProvider _authenticationProvider;

	public InfoControllerBuilder WithOptions(ClusterVNodeOptions options) {
		_options = options;
		return this;

	}

	public InfoControllerBuilder WithFeatures(IDictionary<string, bool> features) {
		_features = features;
		return this;
	}

	public InfoControllerBuilder WithAuthenticationProvider(IAuthenticationProvider authenticationProvider) {
		_authenticationProvider = authenticationProvider;
		return this;
	}

	public InfoController Build() {
		return new InfoController(_options, _features, _authenticationProvider);
	}
}
