// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace EventStore.Common.Utils;

public static class EndpointExtensions {
	public static string HTTP_SCHEMA => Uri.UriSchemeHttp;
	public static string HTTPS_SCHEMA => Uri.UriSchemeHttps;

	public static string ToHttpUrl(this EndPoint endPoint, string schema, string rawUrl = null) =>
		endPoint switch {
			IPEndPoint ipEndPoint => CreateHttpUrl(schema, ipEndPoint.Address.ToString(), ipEndPoint.Port,
				rawUrl != null ? rawUrl.TrimStart('/') : string.Empty),
			DnsEndPoint dnsEndpoint => CreateHttpUrl(schema, dnsEndpoint.Host, dnsEndpoint.Port,
				rawUrl != null ? rawUrl.TrimStart('/') : string.Empty),
			_ => null
		};

	public static string GetHost(this EndPoint endpoint) =>
		endpoint switch {
			IPWithClusterDnsEndPoint ipWithClusterDns => ipWithClusterDns.Address.ToString(),
			IPEndPoint ip => ip.Address.ToString(),
			DnsEndPoint dns => dns.Host,
			_ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint?.GetType(),
				"An invalid endpoint has been provided")
		};

	public static string[] GetOtherNames(this EndPoint endpoint) =>
		endpoint switch {
			IPWithClusterDnsEndPoint ipWithClusterDns => new [] { ipWithClusterDns.ClusterDnsName },
			IPEndPoint => null,
			DnsEndPoint => null,
			_ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint?.GetType(),
				"An invalid endpoint has been provided")
		};

	public static int GetPort(this EndPoint endpoint) =>
		endpoint switch {
			IPEndPoint ip => ip.Port,
			DnsEndPoint dns => dns.Port,
			_ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint?.GetType(),
				"An invalid endpoint has been provided")
		};

	public static IPEndPoint ResolveDnsToIPAddress(this EndPoint endpoint) {
		var entries = Dns.GetHostAddresses(endpoint.GetHost());
		if (entries.Length == 0)
			throw new Exception($"Unable get host addresses for DNS host ({endpoint.GetHost()})");
		var ipaddress = entries.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
		if (ipaddress == null)
			throw new Exception($"Could not get an IPv4 address for host '{endpoint.GetHost()}'");
		return new IPEndPoint(ipaddress, endpoint.GetPort());
	}

	public static EndPoint WithClusterDns(this DnsEndPoint dnsEndPoint, string clusterDns) {
		if (clusterDns != null && IPAddress.TryParse(dnsEndPoint.Host, out var ip))
			return new IPWithClusterDnsEndPoint(ip, clusterDns, dnsEndPoint.Port);

		return dnsEndPoint;
	}

	public static EndPoint WithClusterDns(this IPEndPoint ipEndPoint, string clusterDns) {
		if (clusterDns != null)
			return new IPWithClusterDnsEndPoint(ipEndPoint.Address, clusterDns, ipEndPoint.Port);

		return ipEndPoint;
	}

	public static DnsEndPoint ToDnsEndPoint(this IPEndPoint ipEndPoint) {
		return new DnsEndPoint(ipEndPoint.Address.ToString(), ipEndPoint.Port);
	}

	private static string CreateHttpUrl(string schema, string host, int port, string path) {
		return $"{schema}://{host}:{port}/{path}";
	}

	private static readonly EndPointEqualityComparer EndPointEqualityComparer = new EndPointEqualityComparer();
	public static bool EndPointEquals(this EndPoint x, EndPoint y) => EndPointEqualityComparer.Equals(x, y);
}
