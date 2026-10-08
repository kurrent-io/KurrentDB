// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace KurrentDB.Core.Services.Transport.Http;

public class ClearTextHttpMultiplexingMiddleware(ConnectionDelegate next) {
	private static readonly byte[] Http2Preface = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();
	private static readonly Type ProtocolsFeatureType = typeof(KestrelServerOptions).Assembly
		.GetType("Microsoft.AspNetCore.Server.Kestrel.Core.Internal.HttpProtocolsFeature", throwOnError: true);
	private static readonly object Http1Feature = Activator.CreateInstance(ProtocolsFeatureType, HttpProtocols.Http1);
	private static readonly object Http2Feature = Activator.CreateInstance(ProtocolsFeatureType, HttpProtocols.Http2);

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

				if (result.IsCompleted)
					return false;
			} finally {
				input.AdvanceTo(result.Buffer.Start);
			}
		}
	}

	public async Task OnConnectAsync(ConnectionContext context) {
		var hasHttp2Preface = await HasHttp2Preface(context.Transport.Input);
		// Kestrel's endpoint defaults are shared; protocol detection belongs to this connection only.
		context.Features[ProtocolsFeatureType] = hasHttp2Preface ? Http2Feature : Http1Feature;
		await next(context);
	}
}
