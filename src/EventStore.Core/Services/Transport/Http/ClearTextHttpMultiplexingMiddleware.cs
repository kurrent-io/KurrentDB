// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO.Pipelines;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.CSharp.RuntimeBinder;

namespace EventStore.Core.Services.Transport.Http;

public class ClearTextHttpMultiplexingMiddleware {
	private ConnectionDelegate _next;
	//HTTP/2 prior knowledge-mode connection preface
	private static readonly byte[] _http2Preface = { 0x50, 0x52, 0x49, 0x20, 0x2a, 0x20, 0x48, 0x54, 0x54, 0x50, 0x2f, 0x32, 0x2e, 0x30, 0x0d, 0x0a, 0x0d, 0x0a, 0x53, 0x4d, 0x0d, 0x0a, 0x0d, 0x0a }; //PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n

	public ClearTextHttpMultiplexingMiddleware(ConnectionDelegate next) {
		_next = next;
	}

	private static async Task<bool> HasHttp2Preface(PipeReader input) {
		while (true) {
			var result = await input.ReadAsync();
			try {
				int pos = 0;
				foreach (var x in result.Buffer) {
					for (var i = 0; i < x.Span.Length && pos < _http2Preface.Length; i++) {
						if (_http2Preface[pos] != x.Span[i]) {
							return false;
						}

						pos++;
					}

					if (pos >= _http2Preface.Length) {
						return true;
					}
				}

				if (result.IsCompleted)
					return false;
			} finally {
				input.AdvanceTo(result.Buffer.Start);
			}
		}
	}

	private static void SetProtocols(object target, HttpProtocols protocols) {
		var field = target.GetType().GetField("_endpointDefaultProtocols", BindingFlags.Instance | BindingFlags.NonPublic);
		if (field == null)
			throw new RuntimeBinderException("Couldn't bind to Kestrel's _endpointDefaultProtocols field");
		field.SetValue(target, protocols);
	}

	public async Task OnConnectAsync(ConnectionContext context) {
		var hasHttp2Preface = await HasHttp2Preface(context.Transport.Input);
		SetProtocols(_next.Target, hasHttp2Preface ? HttpProtocols.Http2 : HttpProtocols.Http1);
		await _next(context);
	}
}
