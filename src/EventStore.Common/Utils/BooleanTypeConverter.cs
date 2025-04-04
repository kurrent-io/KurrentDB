using System;
using System.ComponentModel;

namespace EventStore.Common.Utils {
	public class BooleanTypeConverter : BooleanConverter {
		public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType) {
			return sourceType == typeof(string) || sourceType == typeof(int);
		}

		public override object ConvertFrom(ITypeDescriptorContext context, System.Globalization.CultureInfo culture,
			object value) {
			int possibleBinaryBoolean;
			if (value.ToString().Length == 1 &&
				int.TryParse(value.ToString(), out possibleBinaryBoolean)) {
				return possibleBinaryBoolean == 1;
			}

			return base.ConvertFrom(context, culture, value);
		}
	}
}
