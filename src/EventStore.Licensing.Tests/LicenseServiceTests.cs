// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Licensing;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace EventStore.Licensing.Tests;

public class LicenseServiceTests {
	[Fact]
	public async Task self_license_is_valid() {
		var sut = new LicenseService(
			new FakeLifetime(),
			ex => { },
			new AdHocLicenseProvider(new Exception()));
		Assert.True(await sut.SelfLicense.ValidateAsync());
	}

	[Fact]
	public async Task current_license_respects_provider() {
		var licenseProvider = new AdHocLicenseProvider(License.Create([]));
		var sut = new LicenseService(
			new FakeLifetime(),
			ex => { },
			licenseProvider);
		Assert.True(await sut.CurrentLicense!.ValidateAsync());

		licenseProvider.LicenseSubject.OnError(new Exception("an error"));
		Assert.Null(sut.CurrentLicense);
	}

	[Fact]
	public void can_reject() {
		var lifetime = new FakeLifetime();
		var licenseProvider = new AdHocLicenseProvider(License.Create([]));
		var shutdownRequested = false;
		var sut = new LicenseService(
			lifetime,
			requestShutdown: ex => { shutdownRequested = true; },
			licenseProvider);

		sut.RejectLicense(new Exception("an error"));
		lifetime.StartApplication();

		Assert.True(shutdownRequested);
	}

	[Fact]
	public void can_reject_after_startup() {
		var lifetime = new FakeLifetime();
		var licenseProvider = new AdHocLicenseProvider(License.Create([]));
		var shutdownRequested = false;
		var sut = new LicenseService(
			lifetime,
			requestShutdown: ex => { shutdownRequested = true; },
			licenseProvider);

		lifetime.StartApplication();
		sut.RejectLicense(new Exception("an error"));

		Assert.True(shutdownRequested);
	}

	class FakeLifetime : IHostApplicationLifetime {
		readonly CancellationTokenSource _started = new();
		readonly CancellationTokenSource _stopped = new();
		readonly CancellationTokenSource _stopping = new();

		public CancellationToken ApplicationStarted => _started.Token;

		public CancellationToken ApplicationStopped => _stopped.Token;

		public CancellationToken ApplicationStopping => _stopping.Token;

		public void StartApplication() {
			_started.Cancel();
		}

		public void StopApplication() {
			_stopping.Cancel();
			_stopped.Cancel();
		}
	}
}
