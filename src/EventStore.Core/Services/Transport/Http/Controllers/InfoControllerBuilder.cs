// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Common.Options;
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
