// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.ClientAPI.SystemData;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI.SystemData;

[TestFixture, Category("ClientAPI")]
public class clientapi_tcp_package {
	[Test]
	public void should_throw_argument_null_exception_when_created_as_authorized_but_login_not_provided() {
		Assert.Throws<ArgumentNullException>(() =>
			new TcpPackage(TcpCommand.BadRequest, TcpFlags.Authenticated, Guid.NewGuid(), null, "pa$$",
				new byte[] {1, 2, 3}));
	}

	[Test]
	public void should_throw_argument_null_exception_when_created_as_authorized_but_password_not_provided() {
		Assert.Throws<ArgumentNullException>(() =>
			new TcpPackage(TcpCommand.BadRequest, TcpFlags.Authenticated, Guid.NewGuid(), "login", null,
				new byte[] {1, 2, 3}));
	}

	[Test]
	public void should_throw_argument_null_exception_when_created_as_authorized_but_token_not_provided() {
		Assert.Throws<ArgumentNullException>(() =>
			new TcpPackage(TcpCommand.BadRequest, TcpFlags.Authenticated, Guid.NewGuid(), null,
				new byte[] {1, 2, 3}));
	}

	[Test]
	public void should_throw_argument_exception_when_created_as_not_authorized_but_login_is_provided() {
		Assert.Throws<ArgumentException>(() =>
			new TcpPackage(TcpCommand.BadRequest, TcpFlags.None, Guid.NewGuid(), "login", null,
				new byte[] {1, 2, 3}));
	}

	[Test]
	public void should_throw_argument_exception_when_created_as_not_authorized_but_password_is_provided() {
		Assert.Throws<ArgumentException>(() =>
			new TcpPackage(TcpCommand.BadRequest, TcpFlags.None, Guid.NewGuid(), null, "pa$$",
				new byte[] {1, 2, 3}));
	}

	[Test]
	public void should_throw_argument_exception_when_created_as_not_authorized_but_token_is_provided() {
		Assert.Throws<ArgumentException>(() =>
			new TcpPackage(TcpCommand.BadRequest, TcpFlags.None, Guid.NewGuid(), "token",
				new byte[] {1, 2, 3}));
	}

	[Test]
	public void not_authorized_with_data_should_serialize_and_deserialize_correctly() {
		var corrId = Guid.NewGuid();
		var refPkg = new TcpPackage(TcpCommand.BadRequest, TcpFlags.None, corrId, null, null, new byte[] {1, 2, 3});
		var bytes = refPkg.AsArraySegment();

		var pkg = TcpPackage.FromArraySegment(bytes);
		Assert.AreEqual(TcpCommand.BadRequest, pkg.Command);
		Assert.AreEqual(TcpFlags.None, pkg.Flags);
		Assert.AreEqual(corrId, pkg.CorrelationId);
		Assert.False(pkg.Tokens.TryGetValue("uid", out _));
		Assert.False(pkg.Tokens.TryGetValue("pwd", out _));
		Assert.False(pkg.Tokens.TryGetValue("jwt", out _));

		Assert.AreEqual(3, pkg.Data.Count);
		Assert.AreEqual(1, pkg.Data.Array[pkg.Data.Offset + 0]);
		Assert.AreEqual(2, pkg.Data.Array[pkg.Data.Offset + 1]);
		Assert.AreEqual(3, pkg.Data.Array[pkg.Data.Offset + 2]);
	}

	[Test]
	public void not_authorized_with_empty_data_should_serialize_and_deserialize_correctly() {
		var corrId = Guid.NewGuid();
		var refPkg = new TcpPackage(TcpCommand.BadRequest, TcpFlags.None, corrId, null, null, new byte[0]);
		var bytes = refPkg.AsArraySegment();

		var pkg = TcpPackage.FromArraySegment(bytes);
		Assert.AreEqual(TcpCommand.BadRequest, pkg.Command);
		Assert.AreEqual(TcpFlags.None, pkg.Flags);
		Assert.AreEqual(corrId, pkg.CorrelationId);
		Assert.False(pkg.Tokens.TryGetValue("uid", out _));
		Assert.False(pkg.Tokens.TryGetValue("pwd", out _));
		Assert.False(pkg.Tokens.TryGetValue("jwt", out _));

		Assert.AreEqual(0, pkg.Data.Count);
	}

	[Test]
	public void authorized_with_data_should_serialize_and_deserialize_correctly() {
		var corrId = Guid.NewGuid();
		var refPkg = new TcpPackage(TcpCommand.BadRequest, TcpFlags.Authenticated, corrId, "login", "pa$$",
			new byte[] {1, 2, 3});
		var bytes = refPkg.AsArraySegment();

		var pkg = TcpPackage.FromArraySegment(bytes);
		Assert.AreEqual(TcpCommand.BadRequest, pkg.Command);
		Assert.AreEqual(TcpFlags.Authenticated, pkg.Flags);
		Assert.AreEqual(corrId, pkg.CorrelationId);
		Assert.AreEqual("login", pkg.Tokens["uid"]);
		Assert.AreEqual("pa$$", pkg.Tokens["pwd"]);
		Assert.False(pkg.Tokens.TryGetValue("jwt", out _));

		Assert.AreEqual(3, pkg.Data.Count);
		Assert.AreEqual(1, pkg.Data.Array[pkg.Data.Offset + 0]);
		Assert.AreEqual(2, pkg.Data.Array[pkg.Data.Offset + 1]);
		Assert.AreEqual(3, pkg.Data.Array[pkg.Data.Offset + 2]);
	}

	[Test]
	public void authorized_with_empty_data_should_serialize_and_deserialize_correctly() {
		var corrId = Guid.NewGuid();
		var refPkg = new TcpPackage(TcpCommand.BadRequest, TcpFlags.Authenticated, corrId, "login", "pa$$",
			new byte[0]);
		var bytes = refPkg.AsArraySegment();

		var pkg = TcpPackage.FromArraySegment(bytes);
		Assert.AreEqual(TcpCommand.BadRequest, pkg.Command);
		Assert.AreEqual(TcpFlags.Authenticated, pkg.Flags);
		Assert.AreEqual(corrId, pkg.CorrelationId);
		Assert.AreEqual("login", pkg.Tokens["uid"]);
		Assert.AreEqual("pa$$", pkg.Tokens["pwd"]);
		Assert.False(pkg.Tokens.TryGetValue("jwt", out _));

		Assert.AreEqual(0, pkg.Data.Count);
	}

	[Test]
	public void token_authorized_with_data_should_serialize_and_deserialize_correctly() {
		var corrId = Guid.NewGuid();
		var refPkg = new TcpPackage(TcpCommand.BadRequest, TcpFlags.Authenticated, corrId, "token",
			new byte[] {1, 2, 3});
		var bytes = refPkg.AsArraySegment();

		var pkg = TcpPackage.FromArraySegment(bytes);
		Assert.AreEqual(TcpCommand.BadRequest, pkg.Command);
		Assert.AreEqual(TcpFlags.Authenticated, pkg.Flags);
		Assert.AreEqual(corrId, pkg.CorrelationId);
		Assert.AreEqual("token", pkg.Tokens["jwt"]);
		Assert.False(pkg.Tokens.TryGetValue("uid", out _));
		Assert.False(pkg.Tokens.TryGetValue("pwd", out _));

		Assert.AreEqual(3, pkg.Data.Count);
		Assert.AreEqual(1, pkg.Data.Array[pkg.Data.Offset + 0]);
		Assert.AreEqual(2, pkg.Data.Array[pkg.Data.Offset + 1]);
		Assert.AreEqual(3, pkg.Data.Array[pkg.Data.Offset + 2]);
	}

	[Test]
	public void token_authorized_with_empty_data_should_serialize_and_deserialize_correctly() {
		var corrId = Guid.NewGuid();
		var refPkg = new TcpPackage(TcpCommand.BadRequest, TcpFlags.Authenticated, corrId, "token",
			new byte[0]);
		var bytes = refPkg.AsArraySegment();

		var pkg = TcpPackage.FromArraySegment(bytes);
		Assert.AreEqual(TcpCommand.BadRequest, pkg.Command);
		Assert.AreEqual(TcpFlags.Authenticated, pkg.Flags);
		Assert.AreEqual(corrId, pkg.CorrelationId);
		Assert.AreEqual("token", pkg.Tokens["jwt"]);
		Assert.False(pkg.Tokens.TryGetValue("uid", out _));
		Assert.False(pkg.Tokens.TryGetValue("pwd", out _));

		Assert.AreEqual(0, pkg.Data.Count);
	}

	[Test]
	public void should_throw_argument_exception_when_login_too_long() {
		Assert.Throws<ArgumentException>(() => new TcpPackage(TcpCommand.BadRequest, TcpFlags.Authenticated,
			Guid.NewGuid(), new string('*', TcpPackage.MaxLoginLength + 1), "pa$$", new byte[] {1, 2, 3}));
	}

	[Test]
	public void should_throw_argument_exception_when_password_too_long() {
		Assert.Throws<ArgumentException>(() => new TcpPackage(TcpCommand.BadRequest, TcpFlags.Authenticated,
			Guid.NewGuid(), "login", new string('*', TcpPackage.MaxPasswordLength + 1), new byte[] {1, 2, 3}));
	}

	[Test]
	public void should_throw_argument_exception_when_token_too_long() {
		Assert.Throws<ArgumentException>(() => new TcpPackage(TcpCommand.BadRequest, TcpFlags.Authenticated,
			Guid.NewGuid(), new string('*', TcpPackage.MaxTokenLength + 1), new byte[] {1, 2, 3}));
	}
}
