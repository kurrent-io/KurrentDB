// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using EventStore.Transport.Tcp.Formatting;
using EventStore.Transport.Tcp.Framing;
using ILogger = Serilog.ILogger;

namespace EventStore.Transport.Tcp;

public class TcpTypedConnection<T> {
	private static readonly ILogger Log =
		Serilog.Log.ForContext(Serilog.Core.Constants.SourceContextPropertyName, "TcpTypedConnection");

	public event Action<TcpTypedConnection<T>, SocketError> ConnectionClosed;

	private readonly ITcpConnection _connection;
	private readonly IMessageFormatter<T> _formatter;
	private readonly IMessageFramer<ArraySegment<byte>> _framer;

	private Action<TcpTypedConnection<T>, T> _receiveCallback;

	public EndPoint RemoteEndPoint {
		get { return _connection.RemoteEndPoint; }
	}

	public EndPoint LocalEndPoint {
		get { return _connection.LocalEndPoint; }
	}

	public int SendQueueSize {
		get { return _connection.SendQueueSize; }
	}

	public TcpTypedConnection(ITcpConnection connection,
		IMessageFormatter<T> formatter,
		IMessageFramer<ArraySegment<byte>> framer) {
		if (formatter == null)
			throw new ArgumentNullException("formatter");
		if (framer == null)
			throw new ArgumentNullException("framer");

		_connection = connection;
		_formatter = formatter;
		_framer = framer;

		connection.ConnectionClosed += OnConnectionClosed;

		//Setup callback for incoming messages
		framer.RegisterMessageArrivedCallback(IncomingMessageArrived);
	}

	private void OnConnectionClosed(ITcpConnection connection, SocketError socketError) {
		connection.ConnectionClosed -= OnConnectionClosed;

		var handler = ConnectionClosed;
		if (handler != null)
			handler(this, socketError);
	}

	public void EnqueueSend(T message) {
		var data = _formatter.ToArraySegment(message);
		_connection.EnqueueSend(_framer.FrameData(data));
	}

	public void ReceiveAsync(Action<TcpTypedConnection<T>, T> callback) {
		if (_receiveCallback != null)
			throw new InvalidOperationException("ReceiveAsync should be called just once.");

		if (callback == null)
			throw new ArgumentNullException("callback");

		_receiveCallback = callback;

		_connection.ReceiveAsync(OnRawDataReceived);
	}

	private void OnRawDataReceived(ITcpConnection connection, IEnumerable<ArraySegment<byte>> data) {
		try {
			_framer.UnFrameData(data);
		} catch (PackageFramingException exc) {
			Log.Information(exc, "Invalid TCP frame received.");
			Close("Invalid TCP frame received.");
			return;
		}

		connection.ReceiveAsync(OnRawDataReceived);
	}

	private void IncomingMessageArrived(ArraySegment<byte> message) {
		_receiveCallback(this, _formatter.From(message));
	}

	public void Close(string reason = null) {
		_connection.Close(reason);
	}
}
