// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Transport.Http;
using EventStore.Transport.Http.Codecs;
using EventStore.Transport.Http.EntityManagement;
using ClientMessages = EventStore.Core.Messages.ClientMessage.PersistentSubscriptionNackEvents;
using EventStore.Core.Services.PersistentSubscription;
using EventStore.Common.Utils;
using EventStore.Plugins.Authorization;
using EventStore.Core.Settings;
using EventStore.Transport.Http.Atom;
using Microsoft.Extensions.Primitives;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Services.Transport.Http.Controllers;

public class PersistentSubscriptionController : CommunicationController {
	private readonly IHttpForwarder _httpForwarder;
	private readonly IPublisher _networkSendQueue;
	private const int DefaultNumberOfMessagesToGet = 1;
	private static readonly ICodec[] DefaultCodecs = {Codec.Json, Codec.Xml};
	public static readonly char[] ETagSeparatorArray = { ';' };
	private static readonly ICodec[] AtomCodecs = {
		Codec.CompetingXml,
		Codec.CompetingJson,
		Codec.LegacyCompetingJson,
	};

	private static readonly ILogger Log = Serilog.Log.ForContext<PersistentSubscriptionController>();

	public PersistentSubscriptionController(IHttpForwarder httpForwarder, IPublisher publisher,
		IPublisher networkSendQueue)
		: base(publisher) {
		_httpForwarder = httpForwarder;
		_networkSendQueue = networkSendQueue;
	}

	protected override void SubscribeCore(IHttpService service) {
		Register(service, "/subscriptions?offset={offset}&count={count}", HttpMethod.Get, GetAllSubscriptionInfo, Codec.NoCodecs, DefaultCodecs, new Operation(Operations.Subscriptions.Statistics));
		Register(service, "/subscriptions/restart", HttpMethod.Post, RestartPersistentSubscriptions, Codec.NoCodecs, DefaultCodecs, new Operation(Operations.Subscriptions.Restart));
		Register(service, "/subscriptions/{stream}", HttpMethod.Get, GetSubscriptionInfoForStream, Codec.NoCodecs,
			DefaultCodecs, new Operation(Operations.Subscriptions.Statistics));
		Register(service, "/subscriptions/{stream}/{subscription}", HttpMethod.Put, PutSubscription, DefaultCodecs,
			DefaultCodecs, new Operation(Operations.Subscriptions.Create));
		Register(service, "/subscriptions/{stream}/{subscription}", HttpMethod.Post, PostSubscription,
			DefaultCodecs, DefaultCodecs, new Operation(Operations.Subscriptions.Update));
		RegisterUrlBased(service, "/subscriptions/{stream}/{subscription}", HttpMethod.Delete, new Operation(Operations.Subscriptions.Delete), DeleteSubscription);
		Register(service, "/subscriptions/{stream}/{subscription}", HttpMethod.Get, GetNextNMessages,
			Codec.NoCodecs, AtomCodecs, WithParameters(Operations.Subscriptions.ProcessMessages));
		Register(service, "/subscriptions/{stream}/{subscription}?embed={embed}", HttpMethod.Get, GetNextNMessages,
			Codec.NoCodecs, AtomCodecs, WithParameters(Operations.Subscriptions.ProcessMessages));
		Register(service, "/subscriptions/{stream}/{subscription}/{count}?embed={embed}", HttpMethod.Get,
			GetNextNMessages, Codec.NoCodecs, AtomCodecs, WithParameters(Operations.Subscriptions.ProcessMessages));
		Register(service, "/subscriptions/{stream}/{subscription}/info", HttpMethod.Get, GetSubscriptionInfo,
			Codec.NoCodecs, DefaultCodecs, new Operation(Operations.Subscriptions.Statistics));
		RegisterUrlBased(service, "/subscriptions/{stream}/{subscription}/replayParked?stopAt={stopAt}", HttpMethod.Post,
			WithParameters(Operations.Subscriptions.ReplayParked), ReplayParkedMessages);
		RegisterUrlBased(service, "/subscriptions/{stream}/{subscription}/ack/{messageid}", HttpMethod.Post,
			WithParameters(Operations.Subscriptions.ProcessMessages), AckMessage);
		RegisterUrlBased(service, "/subscriptions/{stream}/{subscription}/nack/{messageid}?action={action}",
			HttpMethod.Post, WithParameters(Operations.Subscriptions.ProcessMessages), NackMessage);
		RegisterUrlBased(service, "/subscriptions/{stream}/{subscription}/ack?ids={messageids}", HttpMethod.Post,
			WithParameters(Operations.Subscriptions.ProcessMessages), AckMessages);
		RegisterUrlBased(service, "/subscriptions/{stream}/{subscription}/nack?ids={messageids}&action={action}",
			HttpMethod.Post, WithParameters(Operations.Subscriptions.ProcessMessages), NackMessages);
		//map view parked messages
		Register(service,
			"/subscriptions/viewparkedmessages/{stream}/{group}/{event}/backward/{count}?embed={embed}",
			HttpMethod.Get,
			ViewParkedMessagesBackward, Codec.NoCodecs, AtomCodecs,
			WithParameters(Operations.Subscriptions.Statistics));
		RegisterCustom(service, "/subscriptions/viewparkedmessages/{stream}/{group}/{event}/forward/{count}?embed={embed}", HttpMethod.Get,
			ViewParkedMessagesForward, Codec.NoCodecs, AtomCodecs,  WithParameters(Operations.Subscriptions.Statistics));
	}
	private RequestParams ViewParkedMessagesForward(HttpEntityManager manager, UriTemplateMatch match) {
		var stream = match.BoundVariables["stream"];
		var groupName = match.BoundVariables["group"];
		var evNum = match.BoundVariables["event"];
		var cnt = match.BoundVariables["count"];

		stream = BuildSubscriptionParkedStreamName(stream, groupName);

		long eventNumber;
		int count;
		var embed = GetEmbedLevel(manager, match);

		if (stream.IsEmptyString())
			return SendBadRequest(manager, string.Format("Invalid stream name '{0}'", stream));
		if (evNum.IsEmptyString() || !long.TryParse(evNum, out eventNumber) || eventNumber < 0)
			return SendBadRequest(manager, string.Format("'{0}' is not valid event number", evNum));
		if (cnt.IsEmptyString() || !int.TryParse(cnt, out count) || count <= 0)
			return SendBadRequest(manager,
				string.Format("'{0}' is not valid count. Should be positive integer", cnt));
		bool resolveLinkTos;
		if (!GetResolveLinkTos(manager, out resolveLinkTos, true))
			return SendBadRequest(manager,
				string.Format("{0} header in wrong format.", SystemHeaders.ResolveLinkTos));
		if (!GetRequireLeader(manager, out var requireLeader))
			return SendBadRequest(manager,
				string.Format("{0} header in wrong format.", SystemHeaders.RequireLeader));
		TimeSpan? longPollTimeout;
		if (!GetLongPoll(manager, out longPollTimeout))
			return SendBadRequest(manager, string.Format("{0} header in wrong format.", SystemHeaders.LongPoll));
		var etag = GetETagStreamVersion(manager);

		GetStreamEventsForward(manager, stream, eventNumber, count, resolveLinkTos, requireLeader, etag,
			longPollTimeout, embed);
		return new RequestParams((longPollTimeout ?? TimeSpan.Zero) + ESConsts.HttpTimeout);
	}
	private bool GetLongPoll(HttpEntityManager manager, out TimeSpan? longPollTimeout) {
		longPollTimeout = null;
		var longPollHeader = manager.HttpEntity.Request.GetHeaderValues(SystemHeaders.LongPoll);
		if (StringValues.IsNullOrEmpty(longPollHeader))
			longPollHeader = manager.HttpEntity.Request.GetHeaderValues(SystemHeaders.LegacyLongPoll);

		if (StringValues.IsNullOrEmpty(longPollHeader))
			return true;
		int longPollSec;
		if (int.TryParse(longPollHeader, out longPollSec) && longPollSec > 0) {
			longPollTimeout = TimeSpan.FromSeconds(longPollSec);
			return true;
		}

		return false;
	}
	private void GetStreamEventsForward(HttpEntityManager manager, string stream, long eventNumber, int count,
		bool resolveLinkTos, bool requireLeader, long? etag, TimeSpan? longPollTimeout, EmbedLevel embed) {
		var envelope = new SendToHttpEnvelope(_networkSendQueue,
			manager,
			(ent, msg) => Format.GetStreamEventsForward(ent, msg, embed),
			Configure.GetStreamEventsForward);
		var corrId = Guid.NewGuid();
		Publish(new ClientMessage.ReadStreamEventsForward(corrId, corrId, envelope, stream, eventNumber, count,
			resolveLinkTos, requireLeader, etag, manager.User,
			replyOnExpired: false,
			longPollTimeout: longPollTimeout));
	}
	private void ViewParkedMessagesBackward(HttpEntityManager http, UriTemplateMatch match) {
		var stream = match.BoundVariables["stream"];
		var groupName = match.BoundVariables["group"];
		var evNum = match.BoundVariables["event"];
		var cnt = match.BoundVariables["count"];

		stream = BuildSubscriptionParkedStreamName(stream, groupName);

		long eventNumber = -1;
		int count = AtomSpecs.FeedPageSize;
		var embed = GetEmbedLevel(http, match);

		if (stream.IsEmptyString()) {
			SendBadRequest(http, string.Format("Invalid stream name '{0}'", stream));
			return;
		}

		if (evNum != null && evNum != "head" && (!long.TryParse(evNum, out eventNumber) || eventNumber < 0)) {
			SendBadRequest(http, string.Format("'{0}' is not valid event number", evNum));
			return;
		}

		if (cnt.IsNotEmptyString() && (!int.TryParse(cnt, out count) || count <= 0)) {
			SendBadRequest(http, string.Format("'{0}' is not valid count. Should be positive integer", cnt));
			return;
		}

		bool resolveLinkTos;
		if (!GetResolveLinkTos(http, out resolveLinkTos, true)) {
			SendBadRequest(http, string.Format("{0} header in wrong format.", SystemHeaders.ResolveLinkTos));
			return;
		}

		if (!GetRequireLeader(http, out var requireLeader)) {
			SendBadRequest(http, string.Format("{0} header in wrong format.", SystemHeaders.RequireLeader));
			return;
		}

		bool headOfStream = eventNumber == -1;
		GetStreamEventsBackward(http, stream, eventNumber, count, resolveLinkTos, requireLeader, headOfStream,
			embed);
	}
	private void GetStreamEventsBackward(HttpEntityManager manager, string stream, long eventNumber, int count,
		bool resolveLinkTos, bool requireLeader, bool headOfStream, EmbedLevel embed) {
		var envelope = new SendToHttpEnvelope(_networkSendQueue,
			manager,
			(ent, msg) =>
				Format.GetStreamEventsBackward(ent, msg, embed, headOfStream),
			(args, msg) => Configure.GetStreamEventsBackward(args, msg, headOfStream));
		var corrId = Guid.NewGuid();
		Publish(new ClientMessage.ReadStreamEventsBackward(corrId, corrId, envelope, stream, eventNumber, count,
			resolveLinkTos, requireLeader, GetETagStreamVersion(manager), manager.User));
	}
	private bool GetRequireLeader(HttpEntityManager manager, out bool requireLeader) {
		requireLeader = false;

		var onlyLeader = manager.HttpEntity.Request.GetHeaderValues(SystemHeaders.RequireLeader);
		var onlyLeaderLegacy = manager.HttpEntity.Request.GetHeaderValues(SystemHeaders.LegacyRequireLeader);
		var onlyMaster = manager.HttpEntity.Request.GetHeaderValues(SystemHeaders.RequireMaster);

		if (StringValues.IsNullOrEmpty(onlyLeader) && StringValues.IsNullOrEmpty(onlyMaster) && StringValues.IsNullOrEmpty(onlyLeaderLegacy))
			return true;

		if (string.Equals(onlyLeader, "True", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(onlyLeaderLegacy, "True", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(onlyMaster, "True", StringComparison.OrdinalIgnoreCase)) {
			requireLeader = true;
			return true;
		}

		return string.Equals(onlyLeader, "False", StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(onlyLeaderLegacy, "False", StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(onlyMaster, "False", StringComparison.OrdinalIgnoreCase);
	}
	private bool GetResolveLinkTos(HttpEntityManager manager, out bool resolveLinkTos, bool defaultOption = false) {
		resolveLinkTos = defaultOption;
		var linkToHeader = manager.HttpEntity.Request.GetHeaderValues(SystemHeaders.ResolveLinkTos);
		if (StringValues.IsNullOrEmpty(linkToHeader))
			linkToHeader = manager.HttpEntity.Request.GetHeaderValues(SystemHeaders.LegacyResolveLinkTos);

		if (StringValues.IsNullOrEmpty(linkToHeader))
			return true;
		if (string.Equals(linkToHeader, "False", StringComparison.OrdinalIgnoreCase)) {
			return true;
		}

		if (string.Equals(linkToHeader, "True", StringComparison.OrdinalIgnoreCase)) {
			resolveLinkTos = true;
			return true;
		}

		return false;
	}
	private long? GetETagStreamVersion(HttpEntityManager manager) {
		var etag = manager.HttpEntity.Request.GetHeaderValues("If-None-Match");
		if (!StringValues.IsNullOrEmpty(etag)) {
			// etag format is version;contenttypehash
			var splitted = etag.ToString().Trim('\"').Split(ETagSeparatorArray);
			if (splitted.Length == 2) {
				var typeHash = manager.ResponseCodec.ContentType.GetHashCode()
					.ToString(CultureInfo.InvariantCulture);
				var res = splitted[1] == typeHash && long.TryParse(splitted[0], out var streamVersion)
					? (long?)streamVersion
					: null;
				return res;
			}
		}

		return null;
	}

	static Func<UriTemplateMatch, Operation> WithParameters(OperationDefinition definition) {
		return match => {
			var operation = new Operation(definition);
			var stream = match.BoundVariables["stream"];
			if(!string.IsNullOrEmpty(stream))
				operation = operation.WithParameter(Operations.Subscriptions.Parameters.StreamId(stream));
			var subscription = match.BoundVariables["subscription"];
			if (!string.IsNullOrEmpty(subscription))
				operation = operation.WithParameter(
					Operations.Subscriptions.Parameters.SubscriptionId(subscription));
			return operation;
		};
	}

	private static ClientMessages.NakAction GetNackAction(HttpEntityManager manager, UriTemplateMatch match,
		NakAction nakAction = NakAction.Unknown) {
		var rawValue = match.BoundVariables["action"] ?? string.Empty;
		switch (rawValue.ToLowerInvariant()) {
			case "park": return ClientMessages.NakAction.Park;
			case "retry": return ClientMessages.NakAction.Retry;
			case "skip": return ClientMessages.NakAction.Skip;
			case "stop": return ClientMessages.NakAction.Stop;
			default: return ClientMessages.NakAction.Unknown;
		}
	}

	private void AckMessages(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;
		var envelope = new NoopEnvelope();
		var groupname = match.BoundVariables["subscription"];
		var stream = match.BoundVariables["stream"];
		var messageIds = match.BoundVariables["messageIds"];
		var ids = new List<Guid>();
		foreach (var messageId in messageIds.Split(new[] {','})) {
			Guid id;
			if (!Guid.TryParse(messageId, out id)) {
				http.ReplyStatus(HttpStatusCode.BadRequest, "messageid should be a properly formed guid",
					exception => { });
				return;
			}

			ids.Add(id);
		}

		var cmd = new ClientMessage.PersistentSubscriptionAckEvents(
			Guid.NewGuid(),
			Guid.NewGuid(),
			envelope,
			BuildSubscriptionGroupKey(stream, groupname),
			ids.ToArray(),
			http.User);
		Publish(cmd);
		http.ReplyStatus(HttpStatusCode.Accepted, "", exception => { });
	}

	private void NackMessages(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;
		var envelope = new NoopEnvelope();
		var groupname = match.BoundVariables["subscription"];
		var stream = match.BoundVariables["stream"];
		var messageIds = match.BoundVariables["messageIds"];
		var nakAction = GetNackAction(http, match);
		var ids = new List<Guid>();
		foreach (var messageId in messageIds.Split(new[] {','})) {
			Guid id;
			if (!Guid.TryParse(messageId, out id)) {
				http.ReplyStatus(HttpStatusCode.BadRequest, "messageid should be a properly formed guid",
					exception => { });
				return;
			}

			ids.Add(id);
		}

		var cmd = new ClientMessage.PersistentSubscriptionNackEvents(
			Guid.NewGuid(),
			Guid.NewGuid(),
			envelope,
			BuildSubscriptionGroupKey(stream, groupname),
			"Nacked from HTTP",
			nakAction,
			ids.ToArray(),
			http.User);
		Publish(cmd);
		http.ReplyStatus(HttpStatusCode.Accepted, "", exception => { });
	}

	private static string BuildSubscriptionGroupKey(string stream, string groupName) {
		return stream + "::" + groupName;
	}

	private static string BuildSubscriptionParkedStreamName(string stream, string groupName) {
		return "$persistentsubscription-" + BuildSubscriptionGroupKey(stream, groupName) + "-parked";
	}

	private void AckMessage(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;
		var envelope = new NoopEnvelope();
		var groupname = match.BoundVariables["subscription"];
		var stream = match.BoundVariables["stream"];
		var messageId = match.BoundVariables["messageId"];
		var id = Guid.NewGuid();
		if (!Guid.TryParse(messageId, out id)) {
			http.ReplyStatus(HttpStatusCode.BadRequest, "messageid should be a properly formed guid",
				exception => { });
			return;
		}

		var cmd = new ClientMessage.PersistentSubscriptionAckEvents(
			Guid.NewGuid(),
			Guid.NewGuid(),
			envelope,
			BuildSubscriptionGroupKey(stream, groupname),
			new[] {id},
			http.User);
		Publish(cmd);
		http.ReplyStatus(HttpStatusCode.Accepted, "", exception => { });
	}

	private void NackMessage(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;
		var envelope = new NoopEnvelope();
		var groupname = match.BoundVariables["subscription"];
		var stream = match.BoundVariables["stream"];
		var messageId = match.BoundVariables["messageId"];
		var nakAction = GetNackAction(http, match);
		var id = Guid.NewGuid();
		if (!Guid.TryParse(messageId, out id)) {
			http.ReplyStatus(HttpStatusCode.BadRequest, "messageid should be a properly formed guid",
				exception => { });
			return;
		}

		var cmd = new ClientMessage.PersistentSubscriptionNackEvents(
			Guid.NewGuid(),
			Guid.NewGuid(),
			envelope,
			BuildSubscriptionGroupKey(stream, groupname),
			"Nacked from HTTP",
			nakAction,
			new[] {id},
			http.User);
		Publish(cmd);
		http.ReplyStatus(HttpStatusCode.Accepted, "", exception => { });
	}

	private void ReplayParkedMessages(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;
		var envelope = new SendToHttpEnvelope(
			_networkSendQueue, http,
			(args, message) => http.ResponseCodec.To(message),
			(args, message) => {
				int code;
				var m = message as ClientMessage.ReplayMessagesReceived;
				if (m == null) throw new Exception("unexpected message " + message);
				switch (m.Result) {
					case ClientMessage.ReplayMessagesReceived.ReplayMessagesReceivedResult.Success:
						code = HttpStatusCode.OK;
						break;
					case ClientMessage.ReplayMessagesReceived.ReplayMessagesReceivedResult.DoesNotExist:
						code = HttpStatusCode.NotFound;
						break;
					case ClientMessage.ReplayMessagesReceived.ReplayMessagesReceivedResult.AccessDenied:
						code = HttpStatusCode.Unauthorized;
						break;
					default:
						code = HttpStatusCode.InternalServerError;
						break;
				}

				return new ResponseConfiguration(code, http.ResponseCodec.ContentType,
					http.ResponseCodec.Encoding);
			});
		var groupname = match.BoundVariables["subscription"];
		var stream = match.BoundVariables["stream"];
		var stopAtStr = match.BoundVariables["stopAt"];

		long? stopAt;
		// if stopAt is declared...
		if (stopAtStr != null) {
			// check it is valid
			if (!long.TryParse(stopAtStr, out var stopAtLong) || stopAtLong < 0) {
				http.ReplyStatus(HttpStatusCode.BadRequest, "stopAt should be a properly formed positive long",
					exception => { });
				return;
			}

			stopAt = stopAtLong;
		} else {
			// else it's null
			stopAt = null;
		}

		var cmd = new ClientMessage.ReplayParkedMessages(Guid.NewGuid(), Guid.NewGuid(), envelope, stream,
			groupname, stopAt, http.User);
		Publish(cmd);
	}

	private void PutSubscription(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;
		var groupname = match.BoundVariables["subscription"];
		var stream = match.BoundVariables["stream"];
		var envelope = new SendToHttpEnvelope(
			_networkSendQueue, http,
			(args, message) => http.ResponseCodec.To(message),
			(args, message) => {
				int code;
				var m = message as ClientMessage.CreatePersistentSubscriptionToStreamCompleted;
				if (m == null) throw new Exception("unexpected message " + message);
				switch (m.Result) {
					case ClientMessage.CreatePersistentSubscriptionToStreamCompleted
						.CreatePersistentSubscriptionToStreamResult
						.Success:
						code = HttpStatusCode.Created;
						break;
					case ClientMessage.CreatePersistentSubscriptionToStreamCompleted
						.CreatePersistentSubscriptionToStreamResult
						.AlreadyExists:
						code = HttpStatusCode.Conflict;
						break;
					case ClientMessage.CreatePersistentSubscriptionToStreamCompleted
						.CreatePersistentSubscriptionToStreamResult
						.AccessDenied:
						code = HttpStatusCode.Unauthorized;
						break;
					case ClientMessage.CreatePersistentSubscriptionToStreamCompleted
						.CreatePersistentSubscriptionToStreamResult.Fail:
						code = HttpStatusCode.BadRequest;
						break;
					default:
						code = HttpStatusCode.InternalServerError;
						break;
				}

				return new ResponseConfiguration(code, http.ResponseCodec.ContentType,
					http.ResponseCodec.Encoding,
					new KeyValuePair<string, string>("location",
						MakeUrl(http, "/subscriptions/" + stream + "/" + groupname)));
			});
		http.ReadTextRequestAsync(
			(o, s) => {
				var data = http.RequestCodec.From<SubscriptionConfigData>(s);
				var config = ParseConfig(data);
				if (!ValidateConfig(config, http)) return;
				var message = new ClientMessage.CreatePersistentSubscriptionToStream(Guid.NewGuid(),
					Guid.NewGuid(),
					envelope,
					stream,
					groupname,
					config.ResolveLinktos,
					#pragma warning disable 612
					config.StartPosition != null ? long.Parse(config.StartPosition) : config.StartFrom,
					#pragma warning restore 612
					config.MessageTimeoutMilliseconds,
					config.ExtraStatistics,
					config.MaxRetryCount,
					config.BufferSize,
					config.LiveBufferSize,
					config.ReadBatchSize,
					config.CheckPointAfterMilliseconds,
					config.MinCheckPointCount,
					config.MaxCheckPointCount,
					config.MaxSubscriberCount,
					CalculateNamedConsumerStrategyForOldClients(data),
					http.User);
				Publish(message);
			}, x => Log.Debug(x, "Reply Text Content Failed."));
	}

	private void PostSubscription(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;
		var groupname = match.BoundVariables["subscription"];
		var stream = match.BoundVariables["stream"];
		var envelope = new SendToHttpEnvelope(
			_networkSendQueue, http,
			(args, message) => http.ResponseCodec.To(message),
			(args, message) => {
				int code;
				var m = message as ClientMessage.UpdatePersistentSubscriptionToStreamCompleted;
				if (m == null) throw new Exception("unexpected message " + message);
				switch (m.Result) {
					case ClientMessage.UpdatePersistentSubscriptionToStreamCompleted.UpdatePersistentSubscriptionToStreamResult
						.Success:
						code = HttpStatusCode.OK;
						//TODO competing return uri to subscription
						break;
					case ClientMessage.UpdatePersistentSubscriptionToStreamCompleted.UpdatePersistentSubscriptionToStreamResult
						.DoesNotExist:
						code = HttpStatusCode.NotFound;
						break;
					case ClientMessage.UpdatePersistentSubscriptionToStreamCompleted.UpdatePersistentSubscriptionToStreamResult
						.AccessDenied:
						code = HttpStatusCode.Unauthorized;
						break;
					case ClientMessage.UpdatePersistentSubscriptionToStreamCompleted.UpdatePersistentSubscriptionToStreamResult
						.Fail:
						code = HttpStatusCode.BadRequest;
						break;
					default:
						code = HttpStatusCode.InternalServerError;
						break;
				}

				return new ResponseConfiguration(code, http.ResponseCodec.ContentType,
					http.ResponseCodec.Encoding,
					new KeyValuePair<string, string>("location",
						MakeUrl(http, "/subscriptions/" + stream + "/" + groupname)));
			});
		http.ReadTextRequestAsync(
			(o, s) => {
				var data = http.RequestCodec.From<SubscriptionConfigData>(s);
				var config = ParseConfig(data);
				if (!ValidateConfig(config, http)) return;
				var message = new ClientMessage.UpdatePersistentSubscriptionToStream(Guid.NewGuid(),
					Guid.NewGuid(),
					envelope,
					stream,
					groupname,
					config.ResolveLinktos,
					#pragma warning disable 612
					config.StartPosition != null ? long.Parse(config.StartPosition) : config.StartFrom,
					#pragma warning restore 612
					config.MessageTimeoutMilliseconds,
					config.ExtraStatistics,
					config.MaxRetryCount,
					config.BufferSize,
					config.LiveBufferSize,
					config.ReadBatchSize,
					config.CheckPointAfterMilliseconds,
					config.MinCheckPointCount,
					config.MaxCheckPointCount,
					config.MaxSubscriberCount,
					CalculateNamedConsumerStrategyForOldClients(data),
					http.User);
				Publish(message);
			}, x => Log.Debug(x, "Reply Text Content Failed."));
	}

	private SubscriptionConfigData ParseConfig(SubscriptionConfigData config) {
		if (config == null) {
			return new SubscriptionConfigData();
		}

		return new SubscriptionConfigData {
			ResolveLinktos = config.ResolveLinktos,
			#pragma warning disable 612
			StartFrom = config.StartFrom,
			#pragma warning restore 612
			StartPosition = config.StartPosition,
			MessageTimeoutMilliseconds = config.MessageTimeoutMilliseconds,
			ExtraStatistics = config.ExtraStatistics,
			MaxRetryCount = config.MaxRetryCount,
			BufferSize = config.BufferSize,
			LiveBufferSize = config.LiveBufferSize,
			ReadBatchSize = config.ReadBatchSize,
			CheckPointAfterMilliseconds = config.CheckPointAfterMilliseconds,
			MinCheckPointCount = config.MinCheckPointCount,
			MaxCheckPointCount = config.MaxCheckPointCount,
			MaxSubscriberCount = config.MaxSubscriberCount
		};
	}

	private bool ValidateConfig(SubscriptionConfigData config, HttpEntityManager http) {
		if (config.BufferSize <= 0) {
			SendBadRequest(
				http,
				string.Format(
					"Buffer Size ({0}) must be positive",
					config.BufferSize));
			return false;
		}

		if (config.LiveBufferSize <= 0) {
			SendBadRequest(
				http,
				string.Format(
					"Live Buffer Size ({0}) must be positive",
					config.LiveBufferSize));
			return false;
		}

		if (config.ReadBatchSize <= 0) {
			SendBadRequest(
				http,
				string.Format(
					"Read Batch Size ({0}) must be positive",
					config.ReadBatchSize));
			return false;
		}

		if (!(config.BufferSize > config.ReadBatchSize)) {
			SendBadRequest(
				http,
				string.Format(
					"BufferSize ({0}) must be larger than ReadBatchSize ({1})",
					config.BufferSize, config.ReadBatchSize));
			return false;
		}

		return true;
	}

	private static string CalculateNamedConsumerStrategyForOldClients(SubscriptionConfigData data) {
		var namedConsumerStrategy = data == null ? null : data.NamedConsumerStrategy;
		if (string.IsNullOrEmpty(namedConsumerStrategy)) {
			var preferRoundRobin = data == null || data.PreferRoundRobin;
			namedConsumerStrategy = preferRoundRobin
				? SystemConsumerStrategies.RoundRobin
				: SystemConsumerStrategies.DispatchToSingle;
		}

		return namedConsumerStrategy;
	}

	private void DeleteSubscription(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;
		var envelope = new SendToHttpEnvelope(
			_networkSendQueue, http,
			(args, message) => http.ResponseCodec.To(message),
			(args, message) => {
				int code;
				var m = message as ClientMessage.DeletePersistentSubscriptionToStreamCompleted;
				if (m == null) throw new Exception("unexpected message " + message);
				switch (m.Result) {
					case ClientMessage.DeletePersistentSubscriptionToStreamCompleted.DeletePersistentSubscriptionToStreamResult
						.Success:
						code = HttpStatusCode.OK;
						break;
					case ClientMessage.DeletePersistentSubscriptionToStreamCompleted.DeletePersistentSubscriptionToStreamResult
						.DoesNotExist:
						code = HttpStatusCode.NotFound;
						break;
					case ClientMessage.DeletePersistentSubscriptionToStreamCompleted.DeletePersistentSubscriptionToStreamResult
						.AccessDenied:
						code = HttpStatusCode.Unauthorized;
						break;
					case ClientMessage.DeletePersistentSubscriptionToStreamCompleted.DeletePersistentSubscriptionToStreamResult
						.Fail:
						code = HttpStatusCode.BadRequest;
						break;
					default:
						code = HttpStatusCode.InternalServerError;
						break;
				}

				return new ResponseConfiguration(code, http.ResponseCodec.ContentType,
					http.ResponseCodec.Encoding);
			});
		var groupname = match.BoundVariables["subscription"];
		var stream = match.BoundVariables["stream"];
		var cmd = new ClientMessage.DeletePersistentSubscriptionToStream(Guid.NewGuid(), Guid.NewGuid(), envelope, stream,
			groupname, http.User);
		Publish(cmd);
	}

	private void RestartPersistentSubscriptions(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;

		var envelope = new SendToHttpEnvelope<SubscriptionMessage.PersistentSubscriptionsRestarting>(_networkSendQueue, http,
			(e, message) => e.To("Restarting"),
			(e, message) => {
				switch (message) {
					case SubscriptionMessage.PersistentSubscriptionsRestarting _:
						return Configure.Ok(e.ContentType);
					default:
						return Configure.InternalServerError();
				}
			}, CreateErrorEnvelope(http)
		);

		Publish(new SubscriptionMessage.PersistentSubscriptionsRestart(envelope));
	}

	private void GetAllSubscriptionInfo(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;
		var offsetParam = match.BoundVariables["offset"];
		var countParam = match.BoundVariables["count"];
		if (offsetParam is null && countParam is null) {
			GetSubscriptionInfoUnpaged(http);
		} else {
			GetSubscriptionInfoPaged(http, offsetParam, countParam);
		}

		// old api for backwards compatibility: just returns the list of persistent subscriptions
		void GetSubscriptionInfoUnpaged(HttpEntityManager http) {
			var envelope = new SendToHttpEnvelope(
				_networkSendQueue, http,
				(args, message) =>
					http.ResponseCodec.To(ToSummaryDto(http,
						message as MonitoringMessage.GetPersistentSubscriptionStatsCompleted).ToArray()),
				(args, message) => StatsConfiguration(http, message));
			var cmd = new MonitoringMessage.GetAllPersistentSubscriptionStats(envelope);
			Publish(cmd);
		}

		// new api: returns the list (page) of persistent subscriptions with paging info.
		// count must be provided
		void GetSubscriptionInfoPaged(HttpEntityManager http, string offsetParam, string countParam) {
			int offset = offsetParam is null
				? 0
				: int.TryParse(offsetParam, out var off)
					? off
					: -1;

			if (offset < 0) {
				SendBadRequest(http, $"Offset \"{offsetParam}\" must be a non-negative integer");
				return;
			}

			if (!int.TryParse(countParam, out var count) || count < 1) {
				SendBadRequest(http, $"Count \"{countParam}\" must be a positive integer");
				return;
			}

			var envelope = new SendToHttpEnvelope(
				_networkSendQueue, http,
				(_, message) =>
					http.ResponseCodec.To(ToPagedSummaryDto(
						http, message as MonitoringMessage.GetPersistentSubscriptionStatsCompleted)),
				(_, message) => StatsConfiguration(http, message));
			var cmd = new MonitoringMessage.GetAllPersistentSubscriptionStats(envelope, offset, count);
			Publish(cmd);
		}
	}

	private void GetSubscriptionInfoForStream(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;
		var stream = match.BoundVariables["stream"];
		var envelope = new SendToHttpEnvelope(
			_networkSendQueue, http,
			(args, message) =>
				http.ResponseCodec.To(ToSummaryDto(http,
					message as MonitoringMessage.GetPersistentSubscriptionStatsCompleted).ToArray()),
			(args, message) => StatsConfiguration(http, message));
		var cmd = new MonitoringMessage.GetStreamPersistentSubscriptionStats(envelope, stream);
		Publish(cmd);
	}

	private void GetSubscriptionInfo(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;
		var stream = match.BoundVariables["stream"];
		var groupName = match.BoundVariables["subscription"];
		var envelope = new SendToHttpEnvelope(
			_networkSendQueue, http,
			(args, message) =>
				http.ResponseCodec.To(
					ToDto(http, message as MonitoringMessage.GetPersistentSubscriptionStatsCompleted)
						.FirstOrDefault()),
			(args, message) => StatsConfiguration(http, message));
		var cmd = new MonitoringMessage.GetPersistentSubscriptionStats(envelope, stream, groupName);
		Publish(cmd);
	}

	private IEnvelope CreateErrorEnvelope(HttpEntityManager http) {
		return new SendToHttpEnvelope<SubscriptionMessage.InvalidPersistentSubscriptionsRestart>(
			_networkSendQueue,
			http,
			ErrorFormatter,
			ErrorConfigurator,
			null);
	}

	private ResponseConfiguration ErrorConfigurator(ICodec codec, SubscriptionMessage.InvalidPersistentSubscriptionsRestart message) {
		return new ResponseConfiguration(HttpStatusCode.BadRequest, "Bad Request", ContentType.PlainText,
			Helper.UTF8NoBom);
	}

	private string ErrorFormatter(ICodec codec, SubscriptionMessage.InvalidPersistentSubscriptionsRestart message) {
		return message.Reason;
	}


	private static ResponseConfiguration StatsConfiguration(HttpEntityManager http, Message message) {
		int code;
		var m = message as MonitoringMessage.GetPersistentSubscriptionStatsCompleted;
		if (m == null) throw new Exception("unexpected message " + message);
		switch (m.Result) {
			case MonitoringMessage.GetPersistentSubscriptionStatsCompleted.OperationStatus.Success:
				code = HttpStatusCode.OK;
				break;
			case MonitoringMessage.GetPersistentSubscriptionStatsCompleted.OperationStatus.NotFound:
				code = HttpStatusCode.NotFound;
				break;
			case MonitoringMessage.GetPersistentSubscriptionStatsCompleted.OperationStatus.NotReady:
				code = HttpStatusCode.ServiceUnavailable;
				break;
			default:
				code = HttpStatusCode.InternalServerError;
				break;
		}

		return new ResponseConfiguration(code, http.ResponseCodec.ContentType,
			http.ResponseCodec.Encoding);
	}

	private void GetNextNMessages(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;
		var groupname = match.BoundVariables["subscription"];
		var stream = match.BoundVariables["stream"];
		var cnt = match.BoundVariables["count"];
		var embed = GetEmbedLevel(http, match);
		int count = DefaultNumberOfMessagesToGet;
		if (!cnt.IsEmptyString() && (!int.TryParse(cnt, out count) || count > 100 || count < 1)) {
			SendBadRequest(http,
				string.Format("Message count must be an integer between 1 and 100 'count' ='{0}'", count));
			return;
		}

		var envelope = new SendToHttpEnvelope(
			_networkSendQueue, http,
			(args, message) => Format.ReadNextNPersistentMessagesCompleted(http,
				message as ClientMessage.ReadNextNPersistentMessagesCompleted, stream, groupname, count, embed),
			(args, message) => {
				int code;
				var m = message as ClientMessage.ReadNextNPersistentMessagesCompleted;
				if (m == null) throw new Exception("unexpected message " + message);
				switch (m.Result) {
					case
						ClientMessage.ReadNextNPersistentMessagesCompleted.ReadNextNPersistentMessagesResult
							.Success:
						code = HttpStatusCode.OK;
						break;
					case
						ClientMessage.ReadNextNPersistentMessagesCompleted.ReadNextNPersistentMessagesResult
							.DoesNotExist:
						code = HttpStatusCode.NotFound;
						break;
					case
						ClientMessage.ReadNextNPersistentMessagesCompleted.ReadNextNPersistentMessagesResult
							.AccessDenied:
						code = HttpStatusCode.Unauthorized;
						break;
					case
						ClientMessage.ReadNextNPersistentMessagesCompleted.ReadNextNPersistentMessagesResult
							.Fail:
						code = HttpStatusCode.BadRequest;
						break;
					default:
						code = HttpStatusCode.InternalServerError;
						break;
				}

				return new ResponseConfiguration(code, http.ResponseCodec.ContentType,
					http.ResponseCodec.Encoding);
			});

		var cmd = new ClientMessage.ReadNextNPersistentMessages(
			Guid.NewGuid(),
			Guid.NewGuid(),
			envelope,
			stream,
			groupname,
			count,
			http.User);
		Publish(cmd);
	}

	private static EmbedLevel GetEmbedLevel(HttpEntityManager manager, UriTemplateMatch match,
		EmbedLevel htmlLevel = EmbedLevel.PrettyBody) {
		if (manager.ResponseCodec is IRichAtomCodec)
			return htmlLevel;
		var rawValue = match.BoundVariables["embed"] ?? string.Empty;
		switch (rawValue.ToLowerInvariant()) {
			case "content": return EmbedLevel.Content;
			case "rich": return EmbedLevel.Rich;
			case "body": return EmbedLevel.Body;
			case "pretty": return EmbedLevel.PrettyBody;
			case "tryharder": return EmbedLevel.TryHarder;
			default: return EmbedLevel.None;
		}
	}

	string parkedMessageUriTemplate =
		"/streams/" + Uri.EscapeDataString("$persistentsubscription") + "-{0}::{1}-parked";

	private IEnumerable<SubscriptionInfo> ToDto(HttpEntityManager manager,
		MonitoringMessage.GetPersistentSubscriptionStatsCompleted message) {
		if (message == null) yield break;
		if (message.SubscriptionStats == null) yield break;

		foreach (var stat in message.SubscriptionStats) {
			string escapedStreamId = Uri.EscapeDataString(stat.EventSource);
			string escapedGroupName = Uri.EscapeDataString(stat.GroupName);
			var info = new SubscriptionInfo {
				Links = new List<RelLink>() {
					new RelLink(
						MakeUrl(manager,
							string.Format("/subscriptions/{0}/{1}/info", escapedStreamId, escapedGroupName)),
						"detail"),
					new RelLink(
						MakeUrl(manager,
							string.Format("/subscriptions/{0}/{1}/replayParked", escapedStreamId,
								escapedGroupName)), "replayParked")
				},
				EventStreamId = stat.EventSource,
				GroupName = stat.GroupName,
				Status = stat.Status,
				AverageItemsPerSecond = stat.AveragePerSecond,
				TotalItemsProcessed = stat.TotalItems,
				CountSinceLastMeasurement = stat.CountSinceLastMeasurement,
				#pragma warning disable 612
				LastKnownEventNumber = long.TryParse(stat.LastKnownEventPosition, out var lastKnownMsg) ? lastKnownMsg : 0,
				#pragma warning restore 612
				LastKnownEventPosition = stat.LastKnownEventPosition,
				#pragma warning disable 612
				LastProcessedEventNumber = long.TryParse(stat.LastCheckpointedEventPosition, out var lastProcessedPos) ? lastProcessedPos : 0,
				#pragma warning restore 612
				LastCheckpointedEventPosition = stat.LastCheckpointedEventPosition,
				ReadBufferCount = stat.ReadBufferCount,
				LiveBufferCount = stat.LiveBufferCount,
				RetryBufferCount = stat.RetryBufferCount,
				TotalInFlightMessages = stat.TotalInFlightMessages,
				OutstandingMessagesCount = stat.OutstandingMessagesCount,
				ParkedMessageUri = MakeUrl(manager,
					string.Format(parkedMessageUriTemplate, escapedStreamId, escapedGroupName)),
				GetMessagesUri = MakeUrl(manager,
					string.Format("/subscriptions/{0}/{1}/{2}", escapedStreamId, escapedGroupName,
						DefaultNumberOfMessagesToGet)),
				Config = new SubscriptionConfigData {
					CheckPointAfterMilliseconds = stat.CheckPointAfterMilliseconds,
					BufferSize = stat.BufferSize,
					LiveBufferSize = stat.LiveBufferSize,
					MaxCheckPointCount = stat.MaxCheckPointCount,
					MaxRetryCount = stat.MaxRetryCount,
					MessageTimeoutMilliseconds = stat.MessageTimeoutMilliseconds,
					MinCheckPointCount = stat.MinCheckPointCount,
					NamedConsumerStrategy = stat.NamedConsumerStrategy,
					PreferRoundRobin = stat.NamedConsumerStrategy == SystemConsumerStrategies.RoundRobin,
					ReadBatchSize = stat.ReadBatchSize,
					ResolveLinktos = stat.ResolveLinktos,
					#pragma warning disable 612
					StartFrom = long.TryParse(stat.StartFrom, out var startFrom) ? startFrom : 0,
					#pragma warning restore 612
					StartPosition = stat.StartFrom,
					ExtraStatistics = stat.ExtraStatistics,
					MaxSubscriberCount = stat.MaxSubscriberCount,
				},
				Connections = new List<ConnectionInfo>(),
				ParkedMessageCount = stat.ParkedMessageCount
			};
			if (stat.Connections != null) {
				foreach (var connection in stat.Connections) {
					info.Connections.Add(new ConnectionInfo {
						Username = connection.Username,
						From = connection.From,
						AverageItemsPerSecond = connection.AverageItemsPerSecond,
						CountSinceLastMeasurement = connection.CountSinceLastMeasurement,
						TotalItemsProcessed = connection.TotalItems,
						AvailableSlots = connection.AvailableSlots,
						InFlightMessages = connection.InFlightMessages,
						ExtraStatistics = connection.ObservedMeasurements ?? new List<Measurement>(),
						ConnectionName = connection.ConnectionName,
					});
				}
			}

			yield return info;
		}
	}

	private PagedSubscriptionInfo ToPagedSummaryDto(HttpEntityManager manager,
		MonitoringMessage.GetPersistentSubscriptionStatsCompleted message) {

		if (message is null || message.SubscriptionStats is null) {
			return new PagedSubscriptionInfo();
		}

		var stats = ToSummaryDto(manager, message).ToArray();
		var offset = message.RequestedOffset;
		var count = message.RequestedCount;
		var links = new List<RelLink> {
			new(MakeUrl(manager, "/subscriptions", $"?offset={offset}&count={count}"), "self"),
			new(MakeUrl(manager, "/subscriptions", $"?offset=0&count={count}"), "first"),
		};
		if (offset > 0) {
			var prevOffset = Math.Max(0, offset - count);
			links.Add(new RelLink(MakeUrl(manager, "/subscriptions", $"?offset={prevOffset}&count={count}"), "previous"));
		}
		if (offset + count < message.Total) {
			var nextOffset = offset + count;
			links.Add(new RelLink(MakeUrl(manager, "/subscriptions", $"?offset={nextOffset}&count={count}"), "next"));
		}
		return new PagedSubscriptionInfo {
			Links = links,
			Offset = offset,
			Count = count,
			Total = message.Total,
			Subscriptions = stats,
		};
	}

	private IEnumerable<SubscriptionSummary> ToSummaryDto(HttpEntityManager manager,
		MonitoringMessage.GetPersistentSubscriptionStatsCompleted message) {
		if (message == null) yield break;
		if (message.SubscriptionStats == null) yield break;

		foreach (var stat in message.SubscriptionStats) {
			string escapedStreamId = Uri.EscapeDataString(stat.EventSource);
			string escapedGroupName = Uri.EscapeDataString(stat.GroupName);
			var info = new SubscriptionSummary {
				Links = new List<RelLink>() {
					new RelLink(
						MakeUrl(manager,
							string.Format("/subscriptions/{0}/{1}/info", escapedStreamId, escapedGroupName)),
						"detail"),
				},
				EventStreamId = stat.EventSource,
				GroupName = stat.GroupName,
				Status = stat.Status,
				AverageItemsPerSecond = stat.AveragePerSecond,
				TotalItemsProcessed = stat.TotalItems,
				#pragma warning disable 612
				LastKnownEventNumber = long.TryParse(stat.LastKnownEventPosition, out var lastKnownMsg) ? lastKnownMsg : 0,
				#pragma warning restore 612
				LastKnownEventPosition = stat.LastKnownEventPosition,
				#pragma warning disable 612
				LastProcessedEventNumber = long.TryParse(stat.LastCheckpointedEventPosition, out var lastEventPos) ? lastEventPos : 0,
				#pragma warning restore 612
				LastCheckpointedEventPosition = stat.LastCheckpointedEventPosition,
				ParkedMessageUri = MakeUrl(manager,
					string.Format(parkedMessageUriTemplate, escapedStreamId, escapedGroupName)),
				GetMessagesUri = MakeUrl(manager,
					string.Format("/subscriptions/{0}/{1}/{2}", escapedStreamId, escapedGroupName,
						DefaultNumberOfMessagesToGet)),
				TotalInFlightMessages = stat.TotalInFlightMessages,
			};
			if (stat.Connections != null) {
				info.ConnectionCount = stat.Connections.Count;
			}

			yield return info;
		}
	}

	public class SubscriptionConfigData {
		public bool ResolveLinktos { get; set; }
		[Obsolete] public long StartFrom { get; set; }
		public string StartPosition { get; set; }
		public int MessageTimeoutMilliseconds { get; set; }
		public bool ExtraStatistics { get; set; }
		public int MaxRetryCount { get; set; }
		public int LiveBufferSize { get; set; }
		public int BufferSize { get; set; }
		public int ReadBatchSize { get; set; }
		public bool PreferRoundRobin { get; set; }
		public int CheckPointAfterMilliseconds { get; set; }
		public int MinCheckPointCount { get; set; }
		public int MaxCheckPointCount { get; set; }
		public int MaxSubscriberCount { get; set; }
		public string NamedConsumerStrategy { get; set; }

		public SubscriptionConfigData() {
			#pragma warning disable 612
			StartFrom = 0;
			#pragma warning restore 612
			StartPosition = null;
			MessageTimeoutMilliseconds = 10000;
			MaxRetryCount = 10;
			CheckPointAfterMilliseconds = 1000;
			MinCheckPointCount = 10;
			MaxCheckPointCount = 500;
			MaxSubscriberCount = 10;
			NamedConsumerStrategy = "RoundRobin";

			BufferSize = 500;
			LiveBufferSize = 500;
			ReadBatchSize = 20;
		}
	}

	public class PagedSubscriptionInfo {
		public List<RelLink> Links { get; set; }
		public int Offset { get; set; }
		public int Count { get; set; }
		public int Total { get; set; }
		public IReadOnlyList<SubscriptionSummary> Subscriptions { get; set; }
	}

	public class SubscriptionSummary {
		public List<RelLink> Links { get; set; }
		public string EventStreamId { get; set; }
		public string GroupName { get; set; }
		public string ParkedMessageUri { get; set; }
		public string GetMessagesUri { get; set; }
		public string Status { get; set; }
		public decimal AverageItemsPerSecond { get; set; }
		public long TotalItemsProcessed { get; set; }
		[Obsolete] public long LastProcessedEventNumber { get; set; }
		public string LastCheckpointedEventPosition { get; set; }
		[Obsolete] public long LastKnownEventNumber { get; set; }
		public string LastKnownEventPosition { get; set; }
		public int ConnectionCount { get; set; }
		public int TotalInFlightMessages { get; set; }
	}

	public class SubscriptionInfo {
		public List<RelLink> Links { get; set; }
		public SubscriptionConfigData Config { get; set; }
		public string EventStreamId { get; set; }
		public string GroupName { get; set; }
		public string Status { get; set; }
		public decimal AverageItemsPerSecond { get; set; }
		public string ParkedMessageUri { get; set; }
		public string GetMessagesUri { get; set; }
		public long TotalItemsProcessed { get; set; }
		public long CountSinceLastMeasurement { get; set; }
		[Obsolete] public long LastProcessedEventNumber { get; set; }
		public string LastCheckpointedEventPosition { get; set; }
		[Obsolete] public long LastKnownEventNumber { get; set; }
		public string LastKnownEventPosition { get; set; }
		public int ReadBufferCount { get; set; }
		public long LiveBufferCount { get; set; }
		public int RetryBufferCount { get; set; }
		public int TotalInFlightMessages { get; set; }
		public int OutstandingMessagesCount { get; set; }
		public List<ConnectionInfo> Connections { get; set; }
		public long ParkedMessageCount { get; set; }
	}

	public class ConnectionInfo {
		public string From { get; set; }
		public string Username { get; set; }
		public decimal AverageItemsPerSecond { get; set; }
		public long TotalItemsProcessed { get; set; }
		public long CountSinceLastMeasurement { get; set; }
		public List<Measurement> ExtraStatistics { get; set; }
		public int AvailableSlots { get; set; }
		public int InFlightMessages { get; set; }
		public string ConnectionName { get; set; }
	}
}
