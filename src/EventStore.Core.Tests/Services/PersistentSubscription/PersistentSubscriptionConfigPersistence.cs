// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Text;
using EventStore.Core.Services.PersistentSubscription;
using EventStore.Core.Services.Storage.ReaderIndex;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services;

[TestFixture]
public class PersistentSubscriptionConfigTests {
	[Test]
	public void output_can_be_read_as_input_and_keep_same_values() {
		var config = new PersistentSubscriptionConfig();
		config.Updated = new DateTime(2014, 08, 14);
		config.UpdatedBy = "Greg";
		config.Version = "1";
		config.Entries = new List<PersistentSubscriptionEntry>();
		config.Entries.Add(new PersistentSubscriptionEntry()
			{Group = "foo", ResolveLinkTos = true, Stream = "Stream"});
		var data = config.GetSerializedForm();
		var config2 = PersistentSubscriptionConfig.FromSerializedForm(data);
		Assert.AreEqual(1, config2.Entries.Count);
		Assert.AreEqual(config.Updated, config2.Updated);
		Assert.AreEqual(config.UpdatedBy, config2.UpdatedBy);
	}

	[Test]
	public void bad_json_causes_bad_config_data_exception() {
		var bunkdata = Encoding.UTF8.GetBytes("{'some weird stuff' : 'something'}");
		Assert.Throws<BadConfigDataException>(() => PersistentSubscriptionConfig.FromSerializedForm(bunkdata));
	}

	[Test]
	public void random_bad_data_causes_bad_config_data_exception() {
		var bunkdata = Encoding.UTF8.GetBytes("This ain't even valid json");
		Assert.Throws<BadConfigDataException>(() => PersistentSubscriptionConfig.FromSerializedForm(bunkdata));
	}

	[Test]
	public void event_filter_is_parsed_correctly() {
		var config = new PersistentSubscriptionConfig();
		config.Updated = new DateTime(2014, 08, 14);
		config.UpdatedBy = "admin";
		config.Version = "1";

		var filter = EventFilter.StreamName.Prefixes(true, "test", "blah");
		var entry = new PersistentSubscriptionEntry {
			Group = "foo",
			Stream = "$all",
			Filter = EventFilter.ParseToDto(filter)
		};
		config.Entries = new List<PersistentSubscriptionEntry>{entry};
		var data = config.GetSerializedForm();
		var config2 = PersistentSubscriptionConfig.FromSerializedForm(data);
		var newFilterDto = config2.Entries[0].Filter;
		var (success, reason) = EventFilter.TryParse(newFilterDto, out var newFilter);
		Assert.AreEqual(1, config2.Entries.Count);
		Assert.IsTrue(success);
		Assert.AreEqual(filter.ToString(), newFilter.ToString());
	}

	[Test]
	public void no_event_filter_is_parsed_correctly() {
		var config = new PersistentSubscriptionConfig();
		config.Updated = new DateTime(2014, 08, 14);
		config.UpdatedBy = "admin";
		config.Version = "1";

		var entry = new PersistentSubscriptionEntry {
			Group = "foo",
			Stream = "$all",
			Filter = null
		};
		config.Entries = new List<PersistentSubscriptionEntry>{entry};
		var data = config.GetSerializedForm();
		var config2 = PersistentSubscriptionConfig.FromSerializedForm(data);
		Assert.AreEqual(1, config2.Entries.Count);
		Assert.IsNull(config2.Entries[0].Filter);
	}
}
