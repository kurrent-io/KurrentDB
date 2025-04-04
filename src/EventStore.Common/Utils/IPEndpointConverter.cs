// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.ComponentModel;
using System.Net;

namespace EventStore.Common.Utils;

public class IPEndPointConverter : TypeConverter {
	public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType) {
		return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
	}

	public override object ConvertFrom(ITypeDescriptorContext context, System.Globalization.CultureInfo culture,
		object value) {
		var valueAsString = value as string;
		if (valueAsString != null) {
			var address = valueAsString.Substring(0, valueAsString.LastIndexOf(':'));
			var port = valueAsString.Substring(valueAsString.LastIndexOf(':') + 1);

			return new IPEndPoint(IPAddress.Parse(address), Int32.Parse(port));
		}

		return base.ConvertFrom(context, culture, value);
	}
}
