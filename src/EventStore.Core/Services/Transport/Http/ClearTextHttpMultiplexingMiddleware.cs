// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.CSharp.RuntimeBinder;

namespace EventStore.Core.Services.Transport.Http;

public class ClearTextHttpMultiplexingMiddleware(ConnectionDelegate next) {
	//HTTP/2 prior knowledge-mode connection preface
	private static readonly byte[] Http2Preface = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray(); //PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n

	private static async Task<bool> HasHttp2Preface(PipeReader input) {
		while (true) {
			var result = await input.ReadAsync();
			try {
				int pos = 0;
				foreach (var x in result.Buffer) {
					for (var i = 0; i < x.Span.Length && pos < Http2Preface.Length; i++) {
						if (Http2Preface[pos] != x.Span[i]) {
							return false;
						}

						pos++;
					}

					if (pos >= Http2Preface.Length) {
						return true;
					}
				}

				if (result.IsCompleted) return false;
			} finally {
				input.AdvanceTo(result.Buffer.Start);
			}
		}
	}

	private static void SetProtocols(object target, HttpProtocols protocols) {
		var field = target.GetType().GetField("_endpointDefaultProtocols", BindingFlags.Instance | BindingFlags.NonPublic);
		if (field == null) throw new RuntimeBinderException("Couldn't bind to Kestrel _endpointDefaultProtocols field");
		field.SetValue(target, protocols);
	}

	public async Task OnConnectAsync(ConnectionContext context) {
		var hasHttp2Preface = await HasHttp2Preface(context.Transport.Input);
		SetProtocols(next.Target, hasHttp2Preface ? HttpProtocols.Http2 : HttpProtocols.Http1);
		await next(context);
	}
}
