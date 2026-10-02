// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Configuration;

namespace KurrentDB.Core.Configuration;

public class GossipEndPointConverter : TypeConverter {
	public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
		sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

	public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
		value is string stringValue
			? Parse(stringValue)
			: base.ConvertFrom(context, culture, value);

	// Accepts "host:port", "ipv4:port" and "[ipv6]:port". IPv6 addresses must be bracketed, otherwise
	// the colons inside the address are ambiguous with the port separator.
	public static EndPoint Parse(string value) {
		var separator = value.LastIndexOf(':');
		if (separator < 0)
			throw new("You must specify the port number.");

		var address = value.AsSpan(0, separator);
		var portStr = value.AsSpan(separator + 1);

		if (!int.TryParse(portStr, out var port))
			throw new($"Invalid format for the port number: {portStr}");

		if (address is ['[', .. var bracketed, ']'])
			return IPAddress.TryParse(bracketed, out var ipv6)
				? new IPEndPoint(ipv6, port)
				: throw new($"Invalid IPv6 address: {address}");

		if (address.Contains(':'))
			throw new($"IPv6 addresses must be enclosed in brackets, e.g. [::1]:2113: {value}");

		return IPAddress.TryParse(address, out var ip)
			? new IPEndPoint(ip, port)
			: new DnsEndPoint(address.ToString(), port);
	}

	// IPEndPoint.ToString() brackets IPv6 addresses ("[::1]:2113"), which Parse accepts
	public static string ToString(EndPoint ep) => ep switch {
		IPEndPoint ip => ip.ToString(),
		DnsEndPoint dns => $"{dns.Host}:{dns.Port}",
		_ => ep.ToString() ?? string.Empty,
	};
}

public class GossipSeedConverter : ArrayConverter {
	private static readonly char[] InvalidDelimiters = [';', '\t'];

	public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
		sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

	public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
		=> value is string { } stringValue
			? Parse(stringValue)
			: base.ConvertFrom(context, culture, value);

	public static string ToString(IReadOnlyList<EndPoint> endPoints)
		=> string.Join(',', endPoints.Select(GossipEndPointConverter.ToString));

	public static IReadOnlyList<EndPoint> Parse(string value) {
		if (value.Any(c => InvalidDelimiters.Contains(c)))
			throw new ArgumentException($"Invalid delimiter for gossip seed value: {value}");

		var values = value.Split(',', StringSplitOptions.RemoveEmptyEntries);

		var gossipEndPoints = values
			.Select(GossipEndPointConverter.Parse)
			.ToArray();

		return gossipEndPoints;
	}

	public static IReadOnlyList<EndPoint> Parse(IConfigurationSection section) {
		return section.Get<string[]>() is { Length: > 0 } elements
			? Array.ConvertAll(elements, GossipEndPointConverter.Parse)
			: [];
	}
}

public class IPAddressConverter : TypeConverter {
	public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
		sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

	public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
		value is string stringValue
			? IPAddress.Parse(stringValue)
			: base.ConvertFrom(context, culture, value);
}
