// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Common.Utils;

namespace EventStore.Core.Services.Transport.Tcp;

[Flags]
public enum TcpFlags : byte {
	None = 0x00,
	Authenticated = 0x01,
	TrustedWrite = 0x02
}

public readonly struct TcpPackage {
	private static readonly IReadOnlyDictionary<string, string> NotAuthenticated = new Dictionary<string, string>();
	public const int CommandOffset = 0;
	public const int FlagsOffset = CommandOffset + 1;
	public const int CorrelationOffset = FlagsOffset + 1;
	public const int AuthOffset = CorrelationOffset + 16;

	public const int MandatorySize = AuthOffset;

	public const int MaxLoginLength = 127;
	public const int MaxPasswordLength = 127;
	public const int MaxTokenLength = short.MaxValue - 2;

	public readonly TcpCommand Command;
	public readonly TcpFlags Flags;
	public readonly Guid CorrelationId;
	public readonly ArraySegment<byte> Data;

	private readonly string _login;
	private readonly string _password;
	private readonly string _authToken;

	public IReadOnlyDictionary<string, string> Tokens => (_login, _authToken) switch {
		(null, null) => NotAuthenticated,
		(null, _) => new Dictionary<string, string> {["jwt"] = _authToken},
		_ => new Dictionary<string, string> {["uid"] = _login, ["pwd"] = _password}
	};

	public static TcpPackage FromArraySegment(ArraySegment<byte> data) {
		if (data.Count < MandatorySize)
			throw new ArgumentException($"ArraySegment too short, length: {data.Count}", nameof(data));

		var command = (TcpCommand)data.Array[data.Offset + CommandOffset];
		var flags = (TcpFlags)data.Array[data.Offset + FlagsOffset];

		var guidBytes = new byte[16];
		Buffer.BlockCopy(data.Array, data.Offset + CorrelationOffset, guidBytes, 0, 16);
		var correlationId = new Guid(guidBytes);

		var headerSize = MandatorySize;
		string login = null;
		string pass = null;
		if ((flags & TcpFlags.Authenticated) == 0) {
			return new(command,
				flags,
				correlationId,
				login,
				pass,
				new ArraySegment<byte>(data.Array, data.Offset + headerSize, data.Count - headerSize));
		}

		var firstByte = data.Array[data.Offset + AuthOffset];
		var tokenLength = BitConverter.ToInt16(data.Array, AuthOffset);
		if (Math.Sign(tokenLength) == -1) {
			var token = Helper.UTF8NoBom.GetString(data.Array, AuthOffset + 2, -tokenLength);

			headerSize += token.Length + 2;

			return new(command, flags, correlationId, token, new(data.Array, data.Offset + headerSize, data.Count - headerSize));
		}

		login = Helper.UTF8NoBom.GetString(data.Array, data.Offset + AuthOffset + 1, firstByte);
		var passLen = data.Array[data.Offset + AuthOffset + 1 + firstByte];
		pass = Helper.UTF8NoBom.GetString(data.Array, data.Offset + AuthOffset + 1 + firstByte + 1, passLen);

		headerSize += 1 + firstByte + 1 + passLen;

		return new(command,
			flags,
			correlationId,
			login,
			pass,
			new ArraySegment<byte>(data.Array, data.Offset + headerSize, data.Count - headerSize));
	}

	public TcpPackage(TcpCommand command, Guid correlationId, byte[] data)
		: this(command, TcpFlags.None, correlationId, null, new(data ?? Empty.ByteArray)) {
	}

	public TcpPackage(TcpCommand command, Guid correlationId, ArraySegment<byte> data)
		: this(command, TcpFlags.None, correlationId, null, data) {
	}

	public TcpPackage(TcpCommand command, TcpFlags flags, Guid correlationId, string login, string password, byte[] data)
		: this(command, flags, correlationId, login, password, new ArraySegment<byte>(data ?? Empty.ByteArray)) {
	}

	public TcpPackage(TcpCommand command, TcpFlags flags, Guid correlationId, string login, string password,
		ArraySegment<byte> data) : this(command, flags, correlationId, data) {
		if ((flags & TcpFlags.Authenticated) != 0) {
			Ensure.NotNull(login, nameof(login));
			Ensure.NotNull(password, nameof(password));
			if (Helper.UTF8NoBom.GetByteCount(login) > MaxLoginLength) {
				throw new ArgumentException($"Login length must be less than {MaxLoginLength} bytes.", nameof(login));
			}

			if (Helper.UTF8NoBom.GetByteCount(password) > MaxPasswordLength) {
				throw new ArgumentException($"Password length must be less than {MaxPasswordLength} bytes.", nameof(password));
			}
		} else {
			if (login != null) {
				throw new ArgumentException("Login provided for non-authorized TcpPackage", nameof(login));
			}

			if (password != null) {
				throw new ArgumentException("Password provided for non-authorized TcpPackage", nameof(password));
			}
		}

		_login = login;
		_password = password;
	}

	public TcpPackage(TcpCommand command, TcpFlags flags, Guid correlationId, string authToken,
		ArraySegment<byte> data) : this(command, flags, correlationId, data) {
		if ((flags & TcpFlags.Authenticated) != 0) {
			Ensure.NotNull(authToken, nameof(authToken));

			if (Helper.UTF8NoBom.GetByteCount(authToken) > MaxTokenLength) {
				throw new ArgumentException($"Token length must be smaller than {MaxTokenLength} bytes", nameof(authToken));
			}
		}

		if ((flags & TcpFlags.Authenticated) == 0 && authToken != null) {
			throw new ArgumentException("Token provided for non-authorized TcpPackage", nameof(authToken));
		}

		_authToken = authToken;
	}


	private TcpPackage(TcpCommand command, TcpFlags flags, Guid correlationId, ArraySegment<byte> data) {
		Command = command;
		Flags = flags;
		CorrelationId = correlationId;
		Data = data;
		_login = null;
		_password = null;
		_authToken = null;
	}

	public byte[] AsByteArray() {
		byte[] res;
		if ((Flags & TcpFlags.Authenticated) != 0) {
			if (_authToken == null) {
				var loginLen = Helper.UTF8NoBom.GetByteCount(_login);
				var passLen = Helper.UTF8NoBom.GetByteCount(_password);
				res = new byte[MandatorySize + 2 + loginLen + passLen + Data.Count];
				res[CommandOffset] = (byte)Command;
				res[FlagsOffset] = (byte)Flags;
				Buffer.BlockCopy(CorrelationId.ToByteArray(), 0, res, CorrelationOffset, 16);

				res[AuthOffset] = (byte)loginLen;
				Helper.UTF8NoBom.GetBytes(_login, 0, _login.Length, res, AuthOffset + 1);
				res[AuthOffset + 1 + loginLen] = (byte)passLen;
				Helper.UTF8NoBom.GetBytes(_password, 0, _password.Length, res, AuthOffset + 1 + loginLen + 1);
			} else {
				var authTokenLength = Helper.UTF8NoBom.GetByteCount(_authToken);
				res = new byte[MandatorySize + 2 + authTokenLength + Data.Count];
				res[CommandOffset] = (byte)Command;
				res[FlagsOffset] = (byte)Flags;
				Buffer.BlockCopy(CorrelationId.ToByteArray(), 0, res, CorrelationOffset, 16);
				Buffer.BlockCopy(BitConverter.GetBytes(-Convert.ToInt16(authTokenLength)), 0, res, AuthOffset, 2);
				Helper.UTF8NoBom.GetBytes(_authToken, 0, _authToken.Length, res, AuthOffset + 2);
			}

			Buffer.BlockCopy(Data.Array, Data.Offset, res, res.Length - Data.Count, Data.Count);
			return res;
		}

		res = new byte[MandatorySize + Data.Count];
		res[CommandOffset] = (byte)Command;
		res[FlagsOffset] = (byte)Flags;
		Buffer.BlockCopy(CorrelationId.ToByteArray(), 0, res, CorrelationOffset, 16);
		Buffer.BlockCopy(Data.Array, Data.Offset, res, res.Length - Data.Count, Data.Count);
		return res;
	}

	public ArraySegment<byte> AsArraySegment() => new(AsByteArray());
}
