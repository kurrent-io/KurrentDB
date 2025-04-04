// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using Serilog;

#pragma warning disable 1591

namespace KurrentDB.TestClient;

/// <summary>
/// Data contract for the command-line options accepted by test client.
/// This contract is handled by CommandLine project for .NET
/// </summary>
public sealed record ClientOptions {
	public string Host { get; init; }
	public int TcpPort { get; init; }
	public int HttpPort { get; init; }
	public int Timeout { get; init; }
	public int ReadWindow { get; init; }
	public int WriteWindow { get; init; }
	public int PingWindow { get; init; }
	public string[] Command { get; init; }
	public bool Reconnect { get; set; }

	public bool UseTls { get; init; }
	public bool TlsValidateServer { get; init; }

	public string ConnectionString { get; set; }
	public bool OutputCsv { get; set; }
	public ILogger StatsLog { get; set; }

	public ClientOptions() {
		Command = Array.Empty<string>();
		Host = IPAddress.Loopback.ToString();
		TcpPort = 1113;
		HttpPort = 2113;
		Timeout = -1;
		ReadWindow = 2000;
		WriteWindow = 2000;
		PingWindow = 2000;
		Reconnect = true;
		UseTls = false;
		TlsValidateServer = false;
		ConnectionString = string.Empty;
		OutputCsv = true;
	}

	public override string ToString() {
		return GetType()
			.GetProperties()
			.Aggregate(new StringBuilder(),
				(builder, option) => builder.AppendLine($"{option.Name}: {GetValue(option)}"))
			.ToString();

		object GetValue(PropertyInfo propertyInfo) => propertyInfo.PropertyType.IsArray
			? string.Join(",", ((IEnumerable)propertyInfo.GetValue(this)).OfType<object>())
			: propertyInfo.GetValue(this);
	}
}
