// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net;

namespace EventStore.Core.Configuration;

public class GossipEndPointConverter : TypeConverter {
	public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
		sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

	public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
		value is string stringValue
			? ParseGossipEndPoint(stringValue)
			: base.ConvertFrom(context, culture, value);

	private static EndPoint ParseGossipEndPoint(string value) {
		var parts = value.Split(':', 2);

		if (parts.Length != 2)
			throw new("You must specify the ports in the gossip seed");

		if (!int.TryParse(parts[1], out var port))
			throw new($"Invalid format for gossip seed port: {parts[1]}");

		return IPAddress.TryParse(parts[0], out var ip)
			? new IPEndPoint(ip, port)
			: new DnsEndPoint(parts[0], port);
	}
}

public class GossipSeedConverter : ArrayConverter {
	private static readonly char[] InvalidDelimiters = [';', '\t'];

	private static readonly GossipEndPointConverter _gossipEndPointConverter = new();

	public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
		sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

	public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) {
		if (value is not string stringValue)
			return base.ConvertFrom(context, culture, value);

		if (stringValue.Any(c => InvalidDelimiters.Contains(c)))
			throw new ArgumentException($"Invalid delimiter for gossip seed value: {stringValue}");

		var values = stringValue.Split(',', StringSplitOptions.RemoveEmptyEntries);

		var gossipEndPoints = values
			.Select(x => (EndPoint)_gossipEndPointConverter.ConvertFrom(context, culture, x)!)
			.ToArray();

		return gossipEndPoints;
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
