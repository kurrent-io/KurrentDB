// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Services.PersistentSubscription.ConsumerStrategy;

using System;
using System.IO;
using Data;
using Index.Hashes;
using Newtonsoft.Json;

public class PinnedByCorrelationPersistentSubscriptionConsumerStrategy : PinnedPersistentSubscriptionConsumerStrategy {
	
	public PinnedByCorrelationPersistentSubscriptionConsumerStrategy(IHasher<string> streamHasher) : base(streamHasher) {
	}

	public override string Name {
		get { return SystemConsumerStrategies.PinnedByCorrelation; }
	}


	protected override string GetAssignmentSourceId(ResolvedEvent ev) {
		var eventRecord = ev.Event ?? ev.Link;

		string correlation = CorrelationFromJsonBytes(eventRecord.Metadata);

		if (correlation == null) {
			return GetSourceStreamId(ev);
		}

		return correlation;
	}

	private string CorrelationFromJsonBytes(ReadOnlyMemory<byte> toConvert) {
		using (var reader = new JsonTextReader(new StreamReader(new MemoryStream(toConvert.ToArray())))) {
			if (!reader.Read()) {
				return null;
			}

			while (true) {
				if (!reader.Read()) {
					return null;
				}

				if (reader.TokenType == JsonToken.EndObject) {
					break;
				}

				if (reader.TokenType == JsonToken.PropertyName) {
					if ((string) reader.Value == CorrelationIdPropertyContext.CorrelationIdProperty) {
						reader.Read();

						if (reader.TokenType == JsonToken.String) {
							return (string)reader.Value;
						}
					}
				}
			}
		}

		return null;
	}
}
