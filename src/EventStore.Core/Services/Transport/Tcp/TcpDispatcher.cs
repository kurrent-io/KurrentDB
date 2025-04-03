// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using EventStore.Core.Messaging;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Services.Transport.Tcp;

public abstract class TcpDispatcher : ITcpDispatcher {
	private static readonly ILogger Log = Serilog.Log.ForContext<TcpDispatcher>();

	private readonly Func<TcpPackage, IEnvelope, ClaimsPrincipal, IReadOnlyDictionary<string, string>, TcpConnectionManager, Message>[][] _unwrappers;
	private readonly IDictionary<Type, Func<Message, TcpPackage>>[] _wrappers;

	protected TcpDispatcher() {
		_unwrappers = new Func<TcpPackage, IEnvelope, ClaimsPrincipal, IReadOnlyDictionary<string, string>, TcpConnectionManager, Message>[2][];
		_unwrappers[0] = new Func<TcpPackage, IEnvelope, ClaimsPrincipal, IReadOnlyDictionary<string, string>, TcpConnectionManager, Message>[255];
		_unwrappers[1] = new Func<TcpPackage, IEnvelope, ClaimsPrincipal, IReadOnlyDictionary<string, string>, TcpConnectionManager, Message>[255];

		_wrappers = new IDictionary<Type, Func<Message, TcpPackage>>[2];
		_wrappers[0] = new Dictionary<Type, Func<Message, TcpPackage>>();
		_wrappers[1] = new Dictionary<Type, Func<Message, TcpPackage>>();
	}

	protected void AddWrapper<T>(Func<T, TcpPackage> wrapper, ClientVersion version) where T : Message {
		_wrappers[(byte)version][typeof(T)] = x => wrapper((T)x);
	}

	protected void AddUnwrapper<T>(TcpCommand command, Func<TcpPackage, IEnvelope, T> unwrapper, ClientVersion version) where T : Message {
		_unwrappers[(byte)version][(byte)command] = (pkg, env, _, _, _) => unwrapper(pkg, env);
	}

	protected void AddUnwrapper<T>(TcpCommand command, Func<TcpPackage, IEnvelope, TcpConnectionManager, T> unwrapper, ClientVersion version) where T : Message {
		_unwrappers[(byte)version][(byte)command] = (pkg, env, _, _, conn) => unwrapper(pkg, env, conn);
	}

	protected void AddUnwrapper<T>(TcpCommand command, Func<TcpPackage, IEnvelope, ClaimsPrincipal, TcpConnectionManager, T> unwrapper, ClientVersion version) where T : Message {
		_unwrappers[(byte)version][(byte)command] = (pkg, env, user, _, conn) => unwrapper(pkg, env, user, conn);
	}

	protected void AddUnwrapper<T>(TcpCommand command, Func<TcpPackage, IEnvelope, ClaimsPrincipal, T> unwrapper, ClientVersion version) where T : Message {
		_unwrappers[(byte)version][(byte)command] = (pkg, env, user, _, _) => unwrapper(pkg, env, user);
	}

	protected void AddUnwrapper<T>(TcpCommand command, Func<TcpPackage, IEnvelope, ClaimsPrincipal, IReadOnlyDictionary<string, string>, T> unwrapper, ClientVersion version) where T : Message {
		_unwrappers[(byte)version][(byte)command] = (pkg, env, user, tokens, _) => unwrapper(pkg, env, user, tokens);
	}

	protected void AddUnwrapper<T>(TcpCommand command, Func<TcpPackage, IEnvelope, ClaimsPrincipal, IReadOnlyDictionary<string, string>, TcpConnectionManager, T> unwrapper, ClientVersion version)
		where T : Message {
// ReSharper disable RedundantCast
		_unwrappers[(byte)version][(byte)command] = (Func<TcpPackage, IEnvelope, ClaimsPrincipal, IReadOnlyDictionary<string, string>, TcpConnectionManager, Message>)unwrapper;
// ReSharper restore RedundantCast
	}

	public TcpPackage? WrapMessage(Message message, byte version) {
		ArgumentNullException.ThrowIfNull(message);

		try {
			if (_wrappers[version].TryGetValue(message.GetType(), out var wrapper))
				return wrapper(message);
			if (_wrappers[^1].TryGetValue(message.GetType(), out wrapper))
				return wrapper(message);
		} catch (Exception exc) {
			Log.Error(exc, "Error while wrapping message {message}.", message);
		}

		return null;
	}

	public Message UnwrapPackage(TcpPackage package, IEnvelope envelope, ClaimsPrincipal user,
		IReadOnlyDictionary<string, string> tokens, TcpConnectionManager connection, byte version) {
		ArgumentNullException.ThrowIfNull(envelope);

		var unwrapper = _unwrappers[version][(byte)package.Command] ?? _unwrappers[^1][(byte)package.Command];

		try {
			return unwrapper?.Invoke(package, envelope, user, tokens, connection);
		} catch (Exception exc) {
			Log.Error(exc, "Error while unwrapping TcpPackage with command {command}.", package.Command);
		}

		return null;
	}
}
