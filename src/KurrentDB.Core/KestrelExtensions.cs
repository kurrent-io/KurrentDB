// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.Core;

public static class KestrelExtensions {
	/// <summary>
	/// Applies connection interceptors registered in the DI.
	/// </summary>
	/// <param name="options"></param>
	public static void UseConnectionInterceptors(this ListenOptions options) {
		var interceptors = options.ApplicationServices.GetServices<ConnectionInterceptor>();
		options.Use(interceptors.BuildCallChain);
	}

	private static ConnectionDelegate BuildCallChain(this IEnumerable<ConnectionInterceptor> interceptors,
		ConnectionDelegate current) {
		foreach (var interceptor in interceptors) {
			current = new Tuple<ConnectionDelegate, ConnectionInterceptor>(current, interceptor)
				.InvokeAsync;
		}

		return current;
	}

	private static Task InvokeAsync(this Tuple<ConnectionDelegate, ConnectionInterceptor> args,
		ConnectionContext context)
		=> args.Item2.Invoke(args.Item1, context);
}
