using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace EventStore.Common.Configuration {
	public class DefaultSource : IConfigurationSource {
		private readonly IEnumerable<KeyValuePair<string, object>> _defaultValues;

		public DefaultSource(IEnumerable<KeyValuePair<string, object>> defaultValues) {
			if (defaultValues == null)
				throw new ArgumentNullException(nameof(defaultValues));
			_defaultValues = defaultValues;
		}

		public IConfigurationProvider Build(IConfigurationBuilder builder)
			=> new Default(_defaultValues);
	}
}
