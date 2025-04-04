// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using ILogger = Serilog.ILogger;

namespace EventStore.Transport.Http;

public class IOStreams {
	private static readonly ILogger Log = Serilog.Log.ForContext<IOStreams>();

	public static void SafelyDispose(params Stream[] streams) {
		if (streams == null || streams.Length == 0)
			return;

		foreach (var stream in streams) {
			try {
				if (stream != null)
					stream.Dispose();
			} catch (Exception e) {
				//Exceptions may be thrown when client shutdowned and we were unable to write all the data,
				//Nothing we can do, ignore (another option - globally ignore write errors)
				Log.Information("Error while closing stream : {e}", e.Message);
			}
		}
	}
}
