// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.ComponentModel;
using System.Globalization;
using System.Net;

namespace KurrentDB.Common.Utils;

public class IPEndPointConverter : TypeConverter {
	public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType) {
		return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
	}

	public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture,
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
