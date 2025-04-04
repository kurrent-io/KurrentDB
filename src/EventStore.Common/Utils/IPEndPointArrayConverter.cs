using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;

namespace EventStore.Common.Utils {
	public class IPEndPointArrayConverter : ArrayConverter {
		public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType) {
			return sourceType == typeof(string) || sourceType == typeof(Array) ||
				   base.CanConvertFrom(context, sourceType);
		}

		public override object ConvertFrom(ITypeDescriptorContext context, System.Globalization.CultureInfo culture,
			object value) {
			if (value.GetType() == typeof(string)) {
				var valueAsString = value as string;
				if (valueAsString != null) {
					var ipEndPointList = valueAsString.Split(new[] { "," }, StringSplitOptions.None)
						.Select(x => (IPEndPoint)new IPEndPointConverter().ConvertFrom(x));
					return ipEndPointList.ToArray();
				}
			} else if (value.GetType().IsArray) {
				var values = value as IEnumerable;
				var ipEndPointList = new List<IPEndPoint>();
				foreach (var val in values) {
					ipEndPointList.Add((IPEndPoint)new IPEndPointConverter().ConvertFrom(val));
				}

				return ipEndPointList.ToArray();
			}

			return base.ConvertFrom(context, culture, value);
		}
	}
}
