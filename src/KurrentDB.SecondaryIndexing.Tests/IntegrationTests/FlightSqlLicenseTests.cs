// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Apache.Arrow.Flight;
using Google.Protobuf;
using Grpc.Core;
using KurrentDB.SecondaryIndexing.FlightSql;

namespace KurrentDB.SecondaryIndexing.Tests.IntegrationTests;

public class FlightSqlLicenseTests {
	[Fact]
	public void IsLicensed_DefaultsToTrue() {
		var license = new FlightSqlLicense();
		Assert.True(license.IsLicensed);
	}

	[Fact]
	public void Disable_SetsLicensedFalse() {
		var license = new FlightSqlLicense();
		license.Disable();
		Assert.False(license.IsLicensed);
	}

	[Fact]
	public async Task GetFlightInfo_FailedPrecondition_WhenLicenseRevoked() {
		var server = NewServerWithDisabledLicense();
		var ex = await Assert.ThrowsAsync<RpcException>(() =>
			server.GetFlightInfo(FlightDescriptor.CreateCommandDescriptor([]), context: null!));
		AssertNotLicensed(ex);
	}

	[Fact]
	public async Task DoGet_FailedPrecondition_WhenLicenseRevoked() {
		var server = NewServerWithDisabledLicense();
		var ex = await Assert.ThrowsAsync<RpcException>(() =>
			server.DoGet(new FlightTicket(ByteString.Empty), responseStream: null!, context: null!));
		AssertNotLicensed(ex);
	}

	[Fact]
	public async Task DoAction_FailedPrecondition_WhenLicenseRevoked() {
		var server = NewServerWithDisabledLicense();
		var ex = await Assert.ThrowsAsync<RpcException>(() =>
			server.DoAction(new FlightAction("noop", ByteString.Empty), response: null!, context: null!));
		AssertNotLicensed(ex);
	}

	[Fact]
	public async Task DoPut_FailedPrecondition_WhenLicenseRevoked() {
		var server = NewServerWithDisabledLicense();
		var ex = await Assert.ThrowsAsync<RpcException>(() =>
			server.DoPut(requestStream: null!, responseStream: null!, context: null!));
		AssertNotLicensed(ex);
	}

	[Fact]
	public async Task GetSchema_FailedPrecondition_WhenLicenseRevoked() {
		var server = NewServerWithDisabledLicense();
		var ex = await Assert.ThrowsAsync<RpcException>(() =>
			server.GetSchema(FlightDescriptor.CreateCommandDescriptor([]), context: null!));
		AssertNotLicensed(ex);
	}

	private static FlightSqlServer NewServerWithDisabledLicense() {
		var license = new FlightSqlLicense();
		license.Disable();
		return new FlightSqlServer(engine: null!, authProvider: null!, license);
	}

	private static void AssertNotLicensed(RpcException ex) {
		Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
		Assert.Contains(FlightSqlLicense.Entitlement, ex.Status.Detail);
	}
}
