// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Transport.Http.Controllers;
using EventStore.Transport.Http;
using ReadStreamResult = EventStore.Core.Data.ReadStreamResult;

namespace EventStore.Core.Services.Transport.Http;

public static class Format {
	public static string TextMessage(HttpResponseFormatterArgs entity, Message message) {
		var textMessage = message as HttpMessage.TextMessage;
		return textMessage != null ? entity.ResponseCodec.To(textMessage) : String.Empty;
	}

	public static object EventEntry(HttpResponseFormatterArgs entity, Message message, EmbedLevel embed) {
		var msg = message as ClientMessage.ReadEventCompleted;
		if (msg == null || msg.Result != ReadEventResult.Success || msg.Record.Event == null)
			return entity.ResponseCodec.To(Empty.Result);

		switch (entity.ResponseCodec.ContentType) {
			case ContentType.Atom:
			case ContentType.AtomJson:
			case ContentType.Html:
				return entity.ResponseCodec.To(Convert.ToEntry(msg.Record, entity.ResponseUrl, embed,
					singleEntry: true));
			default:
				return AutoEventConverter.SmartFormat(msg.Record, entity.ResponseCodec);
		}
	}

	public static string GetStreamEventsBackward(HttpResponseFormatterArgs entity, Message message,
		EmbedLevel embed, bool headOfStream) {
		var msg = message as ClientMessage.ReadStreamEventsBackwardCompleted;
		if (msg == null || msg.Result != ReadStreamResult.Success)
			return String.Empty;

		return entity.ResponseCodec.To(
			Convert.ToStreamEventBackwardFeed(msg, entity.ResponseUrl, embed, headOfStream));
	}

	public static string GetStreamEventsForward(HttpResponseFormatterArgs entity, Message message,
		EmbedLevel embed) {
		var msg = message as ClientMessage.ReadStreamEventsForwardCompleted;
		if (msg == null || msg.Result != ReadStreamResult.Success)
			return String.Empty;

		return entity.ResponseCodec.To(Convert.ToStreamEventForwardFeed(msg, entity.ResponseUrl, embed));
	}

	public static string ReadAllEventsBackwardCompleted(HttpResponseFormatterArgs entity, Message message,
		EmbedLevel embed) {
		var msg = message as ClientMessage.ReadAllEventsBackwardCompleted;
		if (msg == null || msg.Result != ReadAllResult.Success)
			return String.Empty;

		return entity.ResponseCodec.To(Convert.ToAllEventsBackwardFeed(msg, entity.ResponseUrl, embed));
	}

	public static string ReadAllEventsBackwardFilteredCompleted(HttpResponseFormatterArgs entity, Message message,
		EmbedLevel embed) {
		var msg = message as ClientMessage.FilteredReadAllEventsBackwardCompleted;
		if (msg == null || msg.Result != FilteredReadAllResult.Success)
			return String.Empty;

		return entity.ResponseCodec.To(Convert.ToFilteredAllEventsBackwardFeed(msg, entity.ResponseUrl, embed));
	}

	public static string ReadAllEventsForwardCompleted(HttpResponseFormatterArgs entity, Message message,
		EmbedLevel embed) {
		var msg = message as ClientMessage.ReadAllEventsForwardCompleted;
		if (msg == null || msg.Result != ReadAllResult.Success)
			return String.Empty;

		return entity.ResponseCodec.To(Convert.ToAllEventsForwardFeed(msg, entity.ResponseUrl, embed));
	}

	public static string ReadAllEventsForwardFilteredCompleted(HttpResponseFormatterArgs entity, Message message,
		EmbedLevel embed) {
		var msg = message as ClientMessage.FilteredReadAllEventsForwardCompleted;
		if (msg == null || msg.Result != FilteredReadAllResult.Success)
			return String.Empty;

		return entity.ResponseCodec.To(Convert.ToAllEventsForwardFilteredFeed(msg, entity.ResponseUrl, embed));
	}

	public static string WriteEventsCompleted(HttpResponseFormatterArgs entity, Message message) {
		return String.Empty;
	}

	public static string DeleteStreamCompleted(HttpResponseFormatterArgs entity, Message message) {
		return String.Empty;
	}

	public static string GetFreshStatsCompleted(HttpResponseFormatterArgs entity, Message message) {
		var completed = message as MonitoringMessage.GetFreshStatsCompleted;
		if (completed == null || !completed.Success)
			return String.Empty;

		return entity.ResponseCodec.To(completed.Stats);
	}

	public static string GetReplicationStatsCompleted(HttpResponseFormatterArgs entity, Message message) {
		if (message.GetType() != typeof(ReplicationMessage.GetReplicationStatsCompleted))
			throw new Exception(string.Format("Unexpected type of Response message: {0}, expected: {1}",
				message.GetType().Name,
				typeof(ReplicationMessage.GetReplicationStatsCompleted).Name));
		var completed = message as ReplicationMessage.GetReplicationStatsCompleted;
		return entity.ResponseCodec.To(completed.ReplicationStats);
	}

	public static string GetFreshTcpConnectionStatsCompleted(HttpResponseFormatterArgs entity, Message message) {
		var completed = message as MonitoringMessage.GetFreshTcpConnectionStatsCompleted;
		if (completed == null)
			return String.Empty;

		return entity.ResponseCodec.To(completed.ConnectionStats);
	}

	public static string SendPublicGossip(HttpResponseFormatterArgs entity, Message message) {
		if (message.GetType() != typeof(GossipMessage.SendClientGossip))
			throw new Exception(string.Format("Unexpected type of response message: {0}, expected: {1}",
				message.GetType().Name,
				typeof(GossipMessage.SendClientGossip).Name));

		var sendPublicGossip = message as GossipMessage.SendClientGossip;
		return sendPublicGossip != null
			? entity.ResponseCodec.To(sendPublicGossip.ClusterInfo)
			: string.Empty;
	}

	public static string ReadNextNPersistentMessagesCompleted(HttpResponseFormatterArgs entity, Message message,
		string streamId, string groupName, int count, EmbedLevel embed) {
		var msg = message as ClientMessage.ReadNextNPersistentMessagesCompleted;
		if (msg == null || msg.Result != ClientMessage.ReadNextNPersistentMessagesCompleted
				.ReadNextNPersistentMessagesResult.Success) {
			return msg != null ? entity.ResponseCodec.To(msg.Reason) : string.Empty;
		}

		return entity.ResponseCodec.To(Convert.ToNextNPersistentMessagesFeed(msg, entity.ResponseUrl, streamId,
			groupName, count, embed));
	}

	public static string GetDescriptionDocument(HttpResponseFormatterArgs entity, string streamId,
		string[] persistentSubscriptionStats) {
		return entity.ResponseCodec.To(Convert.ToDescriptionDocument(entity.RequestedUrl, streamId,
			persistentSubscriptionStats));
	}
}
