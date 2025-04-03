// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using EventStore.Core.Services.UserManagement;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Management;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Other;

[TestFixture]
public class claims_serialization {
	[Test]
	public void should_serialize_principal_name() {
		var principalName = "foo-name";
		var claimsIdentity = new ClaimsIdentity(new Claim[] {
			new(ClaimTypes.Name, principalName),
			new(ClaimTypes.Role, "$admins"),
		});
		var runas = new ProjectionManagementMessage.RunAs(new ClaimsPrincipal(claimsIdentity));
		var sra = SerializedRunAs.SerializePrincipal(runas);
		Assert.AreEqual(principalName, sra.Name);
	}

	[Test]
	public void should_only_serialize_role() {
		var roleClaim = new Claim(ClaimTypes.Role, "$admins");
		var claimsIdentity = new ClaimsIdentity(new [] {
			new(ClaimTypes.Name, "foo-name"),
			roleClaim,
			new("uid", "foo-uid"),
			new("pwd", "foo-pwd")
		});
		var runas = new ProjectionManagementMessage.RunAs(new ClaimsPrincipal(claimsIdentity));
		var sra = SerializedRunAs.SerializePrincipal(runas);
		Assert.AreEqual(1, sra.Roles.Length);
		Assert.AreEqual($"{roleClaim.Type}$$${roleClaim.Value}", sra.Roles[0]);
	}

	[Test]
	public void should_return_null_for_anonymous() {
		var runas = new ProjectionManagementMessage.RunAs(SystemAccounts.Anonymous);
		var sra = SerializedRunAs.SerializePrincipal(runas);
		Assert.IsNull(sra);
	}

	[Test]
	public void should_set_runas_system_for_system_principal() {
		var runas = new ProjectionManagementMessage.RunAs(SystemAccounts.System);
		var sra = SerializedRunAs.SerializePrincipal(runas);
		Assert.AreEqual("$system", sra.Name);
		Assert.IsNull(sra.Roles);
	}
}
