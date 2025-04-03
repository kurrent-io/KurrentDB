// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Plugins.Authorization;
using EventStore.Transport.Http;
using EventStore.Transport.Http.Codecs;
using EventStore.Transport.Http.EntityManagement;

namespace EventStore.Core.Services.Transport.Http.Controllers;

public class StatController : CommunicationController {
	private static readonly ICodec[] SupportedCodecs = new ICodec[] {Codec.Json, Codec.Xml, Codec.ApplicationXml};

	private readonly IPublisher _networkSendQueue;

	public StatController(IPublisher publisher, IPublisher networkSendQueue)
		: base(publisher) {
		_networkSendQueue = networkSendQueue;
	}

	protected override void SubscribeCore(IHttpService service) {
		Ensure.NotNull(service);

		service.RegisterAction(
			new("/stats", HttpMethod.Get, Codec.NoCodecs, SupportedCodecs, new Operation(Operations.Node.Statistics.Read)),
			OnGetFreshStats);
		service.RegisterAction(
			new("/stats/replication", HttpMethod.Get, Codec.NoCodecs, SupportedCodecs, new Operation(Operations.Node.Statistics.Replication)),
			OnGetReplicationStats);
		service.RegisterAction(
			new("/stats/tcp", HttpMethod.Get, Codec.NoCodecs, SupportedCodecs, new Operation(Operations.Node.Statistics.Tcp)),
			OnGetTcpConnectionStats);
		service.RegisterAction(
			new("/stats/{*statPath}", HttpMethod.Get, Codec.NoCodecs, SupportedCodecs, new Operation(Operations.Node.Statistics.Custom)),
			OnGetFreshStats);
	}

	private void OnGetTcpConnectionStats(HttpEntityManager entity, UriTemplateMatch match) {
		var envelope = new SendToHttpEnvelope(_networkSendQueue,
			entity,
			Format.GetFreshTcpConnectionStatsCompleted,
			Configure.GetFreshTcpConnectionStatsCompleted);
		Publish(new MonitoringMessage.GetFreshTcpConnectionStats(envelope));
	}

	private void OnGetFreshStats(HttpEntityManager entity, UriTemplateMatch match) {
		var envelope = new SendToHttpEnvelope(_networkSendQueue,
			entity,
			Format.GetFreshStatsCompleted,
			Configure.GetFreshStatsCompleted);

		var statPath = match.BoundVariables["statPath"];
		var statSelector = GetStatSelector(statPath);

		bool useMetadata;
		if (!bool.TryParse(match.QueryParameters["metadata"], out useMetadata))
			useMetadata = false;

		bool useGrouping;
		if (!bool.TryParse(match.QueryParameters["group"], out useGrouping))
			useGrouping = true;

		if (!useGrouping && !string.IsNullOrEmpty(statPath)) {
			SendBadRequest(entity, "Dynamic stats selection works only with grouping enabled");
			return;
		}

		Publish(new MonitoringMessage.GetFreshStats(envelope, statSelector, useMetadata, useGrouping));
	}

	private static Func<Dictionary<string, object>, Dictionary<string, object>> GetStatSelector(string statPath) {
		if (string.IsNullOrEmpty(statPath))
			return dict => dict;

		//NOTE: this is fix for Mono incompatibility in UriTemplate behavior for /a/b{*C}
		//todo: use IsMono here?
		if (statPath.StartsWith("stats/")) {
			statPath = statPath.Substring(6);
			if (string.IsNullOrEmpty(statPath))
				return dict => dict;
		}

		var groups = statPath.Split('/');

		return dict => {
			Ensure.NotNull(dict, "dictionary");

			foreach (string groupName in groups) {
				object item;
				if (!dict.TryGetValue(groupName, out item))
					return null;

				dict = item as Dictionary<string, object>;

				if (dict == null)
					return null;
			}

			return dict;
		};
	}

	private void OnGetReplicationStats(HttpEntityManager entity, UriTemplateMatch match) {
		var envelope = new SendToHttpEnvelope(_networkSendQueue,
			entity,
			Format.GetReplicationStatsCompleted,
			Configure.GetReplicationStatsCompleted);
		Publish(new ReplicationMessage.GetReplicationStats(envelope));
	}
}
