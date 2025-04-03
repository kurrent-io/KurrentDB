// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Settings;
using EventStore.Transport.Http;
using EventStore.Transport.Http.Atom;
using EventStore.Transport.Http.Codecs;
using EventStore.Transport.Http.EntityManagement;
using Newtonsoft.Json;
using System.Linq;
using System.Threading;
using EventStore.Client.Messages;
using EventStore.Common.Utils;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Util;
using EventStore.Plugins.Authorization;
using Microsoft.Extensions.Primitives;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Services.Transport.Http.Controllers;

public enum EmbedLevel {
	None,
	Content,
	Rich,
	Body,
	PrettyBody,
	TryHarder
}

public class AtomController : CommunicationController {
	public const char ETagSeparator = ';';
	public static readonly char[] ETagSeparatorArray = { ';' };

	private static readonly ILogger Log = Serilog.Log.ForContext<AtomController>();

	private static readonly HtmlFeedCodec HtmlFeedCodec = new HtmlFeedCodec(); // initialization order matters

	private static readonly Func<UriTemplateMatch, Operation> ReadStreamOperation =
		ForStream(Operations.Streams.Read);

	private static readonly Operation RedirectOperation = new Operation(Operations.Node.Redirect);
	private static readonly Operation ReadForAllOperation = new Operation(Operations.Streams.Read).WithParameter(Operations.Streams.Parameters.StreamId(SystemStreams.AllStream));

	private static readonly ICodec[] AtomCodecsWithoutBatches = {
		Codec.EventStoreXmlCodec,
		Codec.KurrentJsonCodec,
		Codec.LegacyEventStoreJsonCodec,
		Codec.Xml,
		Codec.ApplicationXml,
		Codec.Json
	};

	private static readonly ICodec[] AtomCodecs = {
		Codec.DescriptionJson,
		Codec.LegacyDescriptionJson,
		Codec.EventStoreXmlCodec,
		Codec.KurrentJsonCodec,
		Codec.LegacyEventStoreJsonCodec,
		Codec.Xml,
		Codec.ApplicationXml,
		Codec.Json,
		Codec.EventXml,
		Codec.EventJson,
		Codec.LegacyEventJson,
		Codec.EventsXml,
		Codec.EventsJson,
		Codec.LegacyEventsJson,
		Codec.Raw,
	};

	private static readonly ICodec[] AtomWithHtmlCodecs = {
		Codec.DescriptionJson,
		Codec.LegacyDescriptionJson,
		Codec.EventStoreXmlCodec,
		Codec.KurrentJsonCodec,
		Codec.LegacyEventStoreJsonCodec,
		Codec.Xml,
		Codec.ApplicationXml,
		Codec.Json,
		Codec.EventXml,
		Codec.EventJson,
		Codec.LegacyEventJson,
		Codec.EventsXml,
		Codec.EventsJson,
		Codec.LegacyEventsJson,
		HtmlFeedCodec // initialization order matters
	};

	private static readonly ICodec[] DefaultCodecs = {
		Codec.EventStoreXmlCodec,
		Codec.KurrentJsonCodec,
		Codec.LegacyEventStoreJsonCodec,
		Codec.Xml,
		Codec.ApplicationXml,
		Codec.Json,
		Codec.EventXml,
		Codec.EventJson,
		Codec.LegacyEventJson,
		Codec.Raw,
		HtmlFeedCodec // initialization order matters
	};

	private readonly IPublisher _networkSendQueue;
	private readonly TimeSpan _writeTimeout;
	private readonly int _maxAppendEventSize;

	public AtomController(IPublisher publisher, IPublisher networkSendQueue,
		bool disableHTTPCaching, int maxAppendEventSize, TimeSpan writeTimeout) : base(publisher) {
		_networkSendQueue = networkSendQueue;
		_writeTimeout = writeTimeout;
		// Preserve the old behaviour where the Atom controller would not accept events larger than 4mb.
		_maxAppendEventSize = Math.Min(maxAppendEventSize, 4 * 1024 * 1024);

		if (disableHTTPCaching) {
			// ReSharper disable once RedundantNameQualifier
			Transport.Http.Configure.DisableHttpCaching = true;
		}
	}

	protected override void SubscribeCore(IHttpService http) {
		// STREAMS
		Register(http, "/streams/{stream}", HttpMethod.Post, PostEvents, AtomCodecs, AtomCodecs,
			ForStream(Operations.Streams.Write));
		Register(http, "/streams/{stream}", HttpMethod.Delete, DeleteStream, Codec.NoCodecs, AtomCodecs,
			ForStream(Operations.Streams.Delete));

		Register(http, "/streams/{stream}/incoming/{guid}", HttpMethod.Post, PostEventsIdempotent,
			AtomCodecsWithoutBatches, AtomCodecsWithoutBatches, ForStream(Operations.Streams.Write));

		Register(http, "/streams/{stream}/", HttpMethod.Post, RedirectKeepVerb, AtomCodecs, AtomCodecs,
			RedirectOperation);
		Register(http, "/streams/{stream}/", HttpMethod.Delete, RedirectKeepVerb, Codec.NoCodecs, AtomCodecs,
			RedirectOperation);
		Register(http, "/streams/{stream}/", HttpMethod.Get, RedirectKeepVerb, Codec.NoCodecs, AtomCodecs,
			RedirectOperation);

		Register(http, "/streams/{stream}?embed={embed}", HttpMethod.Get, GetStreamEventsBackward, Codec.NoCodecs,
			AtomWithHtmlCodecs, ReadStreamOperation);

		Register(http, "/streams/{stream}/{event}?embed={embed}", HttpMethod.Get, GetStreamEvent, Codec.NoCodecs,
			DefaultCodecs, ReadStreamOperation);
		Register(http, "/streams/{stream}/{event}/{count}?embed={embed}", HttpMethod.Get, GetStreamEventsBackward,
			Codec.NoCodecs, AtomWithHtmlCodecs, ReadStreamOperation);
		Register(http, "/streams/{stream}/{event}/backward/{count}?embed={embed}", HttpMethod.Get,
			GetStreamEventsBackward, Codec.NoCodecs, AtomWithHtmlCodecs, ReadStreamOperation);
		RegisterCustom(http, "/streams/{stream}/{event}/forward/{count}?embed={embed}", HttpMethod.Get,
			GetStreamEventsForward, Codec.NoCodecs, AtomWithHtmlCodecs, ReadStreamOperation);

		// METASTREAMS
		Register(http, "/streams/{stream}/metadata", HttpMethod.Post, PostMetastreamEvent, AtomCodecs, AtomCodecs,
			ForStream(Operations.Streams.MetadataWrite));
		Register(http, "/streams/{stream}/metadata/", HttpMethod.Post, RedirectKeepVerb, AtomCodecs, AtomCodecs,
			RedirectOperation);

		Register(http, "/streams/{stream}/metadata?embed={embed}", HttpMethod.Get, GetMetastreamEvent,
			Codec.NoCodecs, DefaultCodecs, ForStream(Operations.Streams.MetadataRead));
		Register(http, "/streams/{stream}/metadata/?embed={embed}", HttpMethod.Get, RedirectKeepVerb,
			Codec.NoCodecs, DefaultCodecs, RedirectOperation);
		Register(http, "/streams/{stream}/metadata/{event}?embed={embed}", HttpMethod.Get, GetMetastreamEvent,
			Codec.NoCodecs, DefaultCodecs, ForStream(Operations.Streams.MetadataRead));

		Register(http, "/streams/{stream}/metadata/{event}/{count}?embed={embed}", HttpMethod.Get,
			GetMetastreamEventsBackward, Codec.NoCodecs, AtomWithHtmlCodecs, ForStream(Operations.Streams.MetadataRead));
		Register(http, "/streams/{stream}/metadata/{event}/backward/{count}?embed={embed}", HttpMethod.Get,
			GetMetastreamEventsBackward, Codec.NoCodecs, AtomWithHtmlCodecs, ForStream(Operations.Streams.MetadataRead));
		RegisterCustom(http, "/streams/{stream}/metadata/{event}/forward/{count}?embed={embed}", HttpMethod.Get,
			GetMetastreamEventsForward, Codec.NoCodecs, AtomWithHtmlCodecs, ForStream(Operations.Streams.MetadataRead));

		// $ALL Filtered
		const string querystring =
			"?embed={embed}&context={context}&type={type}&data={data}&exclude-system-events={exclude-system-events}";

		Register(http,
			"/streams/$all/filtered" + querystring,
			HttpMethod.Get, GetAllEventsBackwardFiltered, Codec.NoCodecs,
			AtomWithHtmlCodecs, ReadForAllOperation);
		Register(http,
			"/streams/$all/filtered/{position}/{count}" + querystring,
			HttpMethod.Get, GetAllEventsBackwardFiltered,
			Codec.NoCodecs, AtomWithHtmlCodecs, ReadForAllOperation);
		Register(http,
			"/streams/$all/filtered/{position}/backward/{count}" + querystring,
			HttpMethod.Get,
			GetAllEventsBackwardFiltered, Codec.NoCodecs, AtomWithHtmlCodecs, ReadForAllOperation);
		RegisterCustom(http,
			"/streams/$all/filtered/{position}/forward/{count}" + querystring,
			HttpMethod.Get,
			GetAllEventsForwardFiltered, Codec.NoCodecs, AtomWithHtmlCodecs, ReadForAllOperation);
		Register(http,
			"/streams/%24all/filtered" + querystring,
			HttpMethod.Get, GetAllEventsBackwardFiltered, Codec.NoCodecs,
			AtomWithHtmlCodecs, ReadForAllOperation);
		Register(http,
			"/streams/%24all/filtered/{position}/{count}" + querystring,
			HttpMethod.Get, GetAllEventsBackwardFiltered,
			Codec.NoCodecs, AtomWithHtmlCodecs, ReadForAllOperation);
		Register(http,
			"/streams/%24all/filtered/{position}/backward/{count}" + querystring,
			HttpMethod.Get,
			GetAllEventsBackwardFiltered, Codec.NoCodecs, AtomWithHtmlCodecs, ReadForAllOperation);
		RegisterCustom(http,
			"/streams/%24all/filtered/{position}/forward/{count}" + querystring,
			HttpMethod.Get,
			GetAllEventsForwardFiltered, Codec.NoCodecs, AtomWithHtmlCodecs, ReadForAllOperation);

		// $ALL
		Register(http, "/streams/$all/", HttpMethod.Get, RedirectKeepVerb, Codec.NoCodecs, AtomWithHtmlCodecs,
			RedirectOperation);
		Register(http, "/streams/%24all/", HttpMethod.Get, RedirectKeepVerb, Codec.NoCodecs, AtomWithHtmlCodecs,
			RedirectOperation);
		Register(http, "/streams/$all?embed={embed}", HttpMethod.Get, GetAllEventsBackward, Codec.NoCodecs,
			AtomWithHtmlCodecs, ReadForAllOperation);
		Register(http, "/streams/$all/{position}/{count}?embed={embed}", HttpMethod.Get, GetAllEventsBackward,
			Codec.NoCodecs, AtomWithHtmlCodecs, ReadForAllOperation);
		Register(http, "/streams/$all/{position}/backward/{count}?embed={embed}", HttpMethod.Get,
			GetAllEventsBackward, Codec.NoCodecs, AtomWithHtmlCodecs, ReadForAllOperation);
		RegisterCustom(http, "/streams/$all/{position}/forward/{count}?embed={embed}", HttpMethod.Get,
			GetAllEventsForward, Codec.NoCodecs, AtomWithHtmlCodecs, ReadForAllOperation);
		Register(http, "/streams/%24all?embed={embed}", HttpMethod.Get, GetAllEventsBackward, Codec.NoCodecs,
			AtomWithHtmlCodecs, ReadForAllOperation);
		Register(http, "/streams/%24all/{position}/{count}?embed={embed}", HttpMethod.Get, GetAllEventsBackward,
			Codec.NoCodecs, AtomWithHtmlCodecs, ReadForAllOperation);
		Register(http, "/streams/%24all/{position}/backward/{count}?embed={embed}", HttpMethod.Get,
			GetAllEventsBackward, Codec.NoCodecs, AtomWithHtmlCodecs, ReadForAllOperation);
		RegisterCustom(http, "/streams/%24all/{position}/forward/{count}?embed={embed}", HttpMethod.Get,
			GetAllEventsForward, Codec.NoCodecs, AtomWithHtmlCodecs, ReadForAllOperation);
	}

	private static Func<UriTemplateMatch, Operation> ForStream(OperationDefinition definition) {
		return match => {
			var operation = new Operation(definition);
			var stream = match.BoundVariables["stream"];
			if (!string.IsNullOrEmpty(stream)) {
				return operation.WithParameter(Operations.Streams.Parameters.StreamId(stream));
			}

			return operation;
		};
	}

	private bool GetDescriptionDocument(HttpEntityManager manager, UriTemplateMatch match) {
		if (manager.ResponseCodec.ContentType == ContentType.DescriptionDocJson ||
		    manager.ResponseCodec.ContentType == ContentType.LegacyDescriptionDocJson) {
			var stream = match.BoundVariables["stream"];
			var accepts = (manager.HttpEntity.Request.AcceptTypes?.Length ?? 0) == 0 ||
						  manager.HttpEntity.Request.AcceptTypes.Contains(ContentType.Any);
			var responseStatusCode = accepts ? HttpStatusCode.NotAcceptable : HttpStatusCode.OK;
			var responseMessage = manager.HttpEntity.Request.AcceptTypes == null
				? "We are unable to represent the stream in the format requested."
				: "Description Document";
			var envelope = new SendToHttpEnvelope(
				_networkSendQueue, manager,
				(args, message) => {
					var m = message as MonitoringMessage.GetPersistentSubscriptionStatsCompleted;
					if (m == null)
						throw new Exception("Could not get subscriptions for stream " + stream);

					string[] persistentSubscriptionGroups = null;
					if (m.Result == MonitoringMessage.GetPersistentSubscriptionStatsCompleted.OperationStatus
							.Success) {
						persistentSubscriptionGroups = m.SubscriptionStats.Select(x => x.GroupName).ToArray();
					}

					manager.ReplyTextContent(
						Format.GetDescriptionDocument(manager, stream, persistentSubscriptionGroups),
						responseStatusCode, responseMessage,
						manager.ResponseCodec.ContentType,
						null,
						e => Log.Error(e, "Error while writing HTTP response"));
					return String.Empty;
				},
				(args, message) => new ResponseConfiguration(HttpStatusCode.OK, manager.ResponseCodec.ContentType,
					manager.ResponseCodec.Encoding));
			var cmd = new MonitoringMessage.GetStreamPersistentSubscriptionStats(envelope, stream);
			Publish(cmd);
			return true;
		}

		return false;
	}

	private void RedirectKeepVerb(HttpEntityManager httpEntity, UriTemplateMatch uriTemplateMatch) {
		var original = uriTemplateMatch.RequestUri.ToString();
		var header = new[] {
			new KeyValuePair<string, string>("Location", original.Substring(0, original.Length - 1)),
			new KeyValuePair<string, string>("Cache-Control", "max-age=31536000, public"),
		};
		httpEntity.ReplyTextContent("Moved Permanently", HttpStatusCode.RedirectKeepVerb, "", "", header, e => { });
	}

	// STREAMS
	private void PostEvents(HttpEntityManager manager, UriTemplateMatch match) {
		var stream = match.BoundVariables["stream"];
		if (stream.IsEmptyString()) {
			SendBadRequest(manager, string.Format("Invalid request. Stream must be non-empty string"));
			return;
		}

		string includedType;
		if (!GetIncludedType(manager, out includedType)) {
			SendBadRequest(manager, string.Format("{0} header in wrong format.", SystemHeaders.EventType));
			return;
		}

		if (!manager.RequestCodec.HasEventTypes && includedType == null) {
			SendBadRequest(manager,
				$"Must include an event type with the request either in body or as {SystemHeaders.EventType} header.");
			return;
		}

		Guid includedId;
		if (!GetIncludedId(manager, out includedId)) {
			SendBadRequest(manager, string.Format("{0} header in wrong format.", SystemHeaders.EventId));
			return;
		}

		if (!manager.RequestCodec.HasEventIds && includedId == Guid.Empty) {
			var uri = new Uri(new Uri(match.RequestUri + "/"), "incoming/" + Guid.NewGuid()).ToString();
			var header = new[]
				{new KeyValuePair<string, string>("Location", uri)};
			manager.ReplyTextContent("Forwarding to idempotent URI", HttpStatusCode.RedirectKeepVerb,
				"Temporary Redirect", ContentType.PlainText, header, e => { });
			return;
		}

		long expectedVersion;
		if (!GetExpectedVersion(manager, out expectedVersion)) {
			SendBadRequest(manager, string.Format("{0} header in wrong format.", SystemHeaders.ExpectedVersion));
			return;
		}

		if (!GetRequireLeader(manager, out var requireLeader)) {
			SendBadRequest(manager, string.Format("{0} header in wrong format.", SystemHeaders.RequireLeader));
			return;
		}

		PostEntry(manager, expectedVersion, requireLeader, stream, includedId, includedType);
	}

	private void PostEventsIdempotent(HttpEntityManager manager, UriTemplateMatch match) {
		var stream = match.BoundVariables["stream"];
		var guid = match.BoundVariables["guid"];
		Guid id;
		if (!Guid.TryParse(guid, out id)) {
			SendBadRequest(manager, string.Format("Invalid request. Unable to parse guid"));
			return;
		}

		if (stream.IsEmptyString()) {
			SendBadRequest(manager, string.Format("Invalid request. Stream must be non-empty string"));
			return;
		}

		string includedType;
		if (!GetIncludedType(manager, out includedType)) {
			SendBadRequest(manager, string.Format("{0} header in wrong format.", SystemHeaders.EventType));
			return;
		}

		long expectedVersion;
		if (!GetExpectedVersion(manager, out expectedVersion)) {
			SendBadRequest(manager, string.Format("{0} header in wrong format.", SystemHeaders.ExpectedVersion));
			return;
		}

		if (!GetRequireLeader(manager, out var requireLeader)) {
			SendBadRequest(manager, string.Format("{0} header in wrong format.", SystemHeaders.RequireLeader));
			return;
		}

		PostEntry(manager, expectedVersion, requireLeader, stream, id, includedType);
	}

	private void DeleteStream(HttpEntityManager manager, UriTemplateMatch match) {
		var stream = match.BoundVariables["stream"];
		if (stream.IsEmptyString()) {
			SendBadRequest(manager, string.Format("Invalid stream name '{0}'", stream));
			return;
		}

		long expectedVersion;
		if (!GetExpectedVersion(manager, out expectedVersion)) {
			SendBadRequest(manager, string.Format("{0} header in wrong format.", SystemHeaders.ExpectedVersion));
			return;
		}

		if (!GetRequireLeader(manager, out var requireLeader)) {
			SendBadRequest(manager, string.Format("{0} header in wrong format.", SystemHeaders.RequireLeader));
			return;
		}

		bool hardDelete;
		if (!GetHardDelete(manager, out hardDelete)) {
			SendBadRequest(manager, string.Format("{0} header in wrong format.", SystemHeaders.HardDelete));
			return;
		}

		var cts = CancellationTokenSource.CreateLinkedTokenSource(manager.HttpEntity.Context.RequestAborted);
		var envelope = new SendToHttpEnvelope(_networkSendQueue, manager, Format.DeleteStreamCompleted,
			ConfigureResponse);
		var corrId = Guid.NewGuid();
		cts.CancelAfter(_writeTimeout);
		Publish(new ClientMessage.DeleteStream(corrId, corrId, envelope, requireLeader, stream, expectedVersion,
			hardDelete, manager.User, cancellationToken: manager.HttpEntity.Context.RequestAborted));

		ResponseConfiguration ConfigureResponse(HttpResponseConfiguratorArgs args, Message message) {
			cts.Dispose();
			return Configure.DeleteStreamCompleted(args, message);
		}
	}

	private void GetStreamEvent(HttpEntityManager manager, UriTemplateMatch match) {
		var stream = match.BoundVariables["stream"];
		var evNum = match.BoundVariables["event"];

		long eventNumber = -1;
		var embed = GetEmbedLevel(manager, match, EmbedLevel.TryHarder);

		if (stream.IsEmptyString()) {
			SendBadRequest(manager, string.Format("Invalid stream name '{0}'", stream));
			return;
		}

		if (evNum != "head" && (!long.TryParse(evNum, out eventNumber) || eventNumber < 0)) {
			SendBadRequest(manager, string.Format("'{0}' is not valid event number", evNum));
			return;
		}

		bool resolveLinkTos;
		if (!GetResolveLinkTos(manager, out resolveLinkTos, true)) {
			SendBadRequest(manager, string.Format("{0} header in wrong format.", SystemHeaders.ResolveLinkTos));
			return;
		}

		if (!GetRequireLeader(manager, out var requireLeader)) {
			SendBadRequest(manager, string.Format("{0} header in wrong format.", SystemHeaders.RequireLeader));
			return;
		}

		GetStreamEvent(manager, stream, eventNumber, resolveLinkTos, requireLeader, embed);
	}

	private void GetStreamEventsBackward(HttpEntityManager manager, UriTemplateMatch match) {
		if (GetDescriptionDocument(manager, match))
			return;

		var stream = match.BoundVariables["stream"];
		var evNum = match.BoundVariables["event"];
		var cnt = match.BoundVariables["count"];

		long eventNumber = -1;
		int count = AtomSpecs.FeedPageSize;
		var embed = GetEmbedLevel(manager, match);

		if (stream.IsEmptyString()) {
			SendBadRequest(manager, string.Format("Invalid stream name '{0}'", stream));
			return;
		}

		if (evNum != null && evNum != "head" && (!long.TryParse(evNum, out eventNumber) || eventNumber < 0)) {
			SendBadRequest(manager, string.Format("'{0}' is not valid event number", evNum));
			return;
		}

		if (cnt.IsNotEmptyString() && (!int.TryParse(cnt, out count) || count <= 0)) {
			SendBadRequest(manager, string.Format("'{0}' is not valid count. Should be positive integer", cnt));
			return;
		}

		bool resolveLinkTos;
		if (!GetResolveLinkTos(manager, out resolveLinkTos, true)) {
			SendBadRequest(manager, string.Format("{0} header in wrong format.", SystemHeaders.ResolveLinkTos));
			return;
		}

		if (!GetRequireLeader(manager, out var requireLeader)) {
			SendBadRequest(manager, string.Format("{0} header in wrong format.", SystemHeaders.RequireLeader));
			return;
		}

		bool headOfStream = eventNumber == -1;
		GetStreamEventsBackward(manager, stream, eventNumber, count, resolveLinkTos, requireLeader, headOfStream,
			embed);
	}

	private RequestParams GetStreamEventsForward(HttpEntityManager manager, UriTemplateMatch match) {
		var stream = match.BoundVariables["stream"];
		var evNum = match.BoundVariables["event"];
		var cnt = match.BoundVariables["count"];

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

	// METASTREAMS
	private void PostMetastreamEvent(HttpEntityManager manager, UriTemplateMatch match) {
		var stream = match.BoundVariables["stream"];
		if (stream.IsEmptyString() || SystemStreams.IsMetastream(stream)) {
			SendBadRequest(manager,
				string.Format("Invalid request. Stream must be non-empty string and should not be metastream"));
			return;
		}

		Guid includedId;
		if (!GetIncludedId(manager, out includedId)) {
			SendBadRequest(manager, string.Format("{0} header in wrong format.", SystemHeaders.EventId));
			return;
		}

		string foo;
		GetIncludedType(manager, out foo);
		if (!(foo == null || foo == SystemEventTypes.StreamMetadata)) {
			SendBadRequest(manager, "Bad Request. You should not include an event type for metadata.");
			return;
		}

		const string includedType = SystemEventTypes.StreamMetadata;
		long expectedVersion;
		if (!GetExpectedVersion(manager, out expectedVersion)) {
			SendBadRequest(manager, string.Format("{0} header in wrong format.", SystemHeaders.ExpectedVersion));
			return;
		}

		if (!GetRequireLeader(manager, out var requireLeader)) {
			SendBadRequest(manager, string.Format("{0} header in wrong format.", SystemHeaders.RequireLeader));
			return;
		}

		PostEntry(manager, expectedVersion, requireLeader, SystemStreams.MetastreamOf(stream), includedId,
			includedType);
	}

	private void GetMetastreamEvent(HttpEntityManager manager, UriTemplateMatch match) {
		var stream = match.BoundVariables["stream"];
		var evNum = match.BoundVariables["event"];

		long eventNumber = -1;
		var embed = GetEmbedLevel(manager, match, EmbedLevel.TryHarder);

		if (stream.IsEmptyString() || SystemStreams.IsMetastream(stream)) {
			SendBadRequest(manager, "Stream must be non-empty string and should not be metastream");
			return;
		}

		if (evNum != null && evNum != "head" && (!long.TryParse(evNum, out eventNumber) || eventNumber < 0)) {
			SendBadRequest(manager, string.Format("'{0}' is not valid event number", evNum));
			return;
		}

		bool resolveLinkTos;
		if (!GetResolveLinkTos(manager, out resolveLinkTos)) {
			SendBadRequest(manager, string.Format("{0} header in wrong format.", SystemHeaders.ResolveLinkTos));
			return;
		}

		if (!GetRequireLeader(manager, out var requireLeader)) {
			SendBadRequest(manager, string.Format("{0} header in wrong format.", SystemHeaders.RequireLeader));
			return;
		}

		GetStreamEvent(manager, SystemStreams.MetastreamOf(stream), eventNumber, resolveLinkTos, requireLeader,
			embed);
	}

	private void GetMetastreamEventsBackward(HttpEntityManager manager, UriTemplateMatch match) {
		var stream = match.BoundVariables["stream"];
		var evNum = match.BoundVariables["event"];
		var cnt = match.BoundVariables["count"];

		long eventNumber = -1;
		int count = AtomSpecs.FeedPageSize;
		var embed = GetEmbedLevel(manager, match);

		if (stream.IsEmptyString() || SystemStreams.IsMetastream(stream)) {
			SendBadRequest(manager, string.Format("Invalid stream name '{0}'", stream));
			return;
		}

		if (evNum != null && evNum != "head" && (!long.TryParse(evNum, out eventNumber) || eventNumber < 0)) {
			SendBadRequest(manager, string.Format("'{0}' is not valid event number", evNum));
			return;
		}

		if (cnt.IsNotEmptyString() && (!int.TryParse(cnt, out count) || count <= 0)) {
			SendBadRequest(manager, string.Format("'{0}' is not valid count. Should be positive integer", cnt));
			return;
		}

		bool resolveLinkTos;
		if (!GetResolveLinkTos(manager, out resolveLinkTos)) {
			SendBadRequest(manager, string.Format("{0} header in wrong format.", SystemHeaders.ResolveLinkTos));
			return;
		}

		if (!GetRequireLeader(manager, out var requireLeader)) {
			SendBadRequest(manager, string.Format("{0} header in wrong format.", SystemHeaders.RequireLeader));
			return;
		}

		bool headOfStream = eventNumber == -1;
		GetStreamEventsBackward(manager, SystemStreams.MetastreamOf(stream), eventNumber, count,
			resolveLinkTos, requireLeader, headOfStream, embed);
	}

	private RequestParams GetMetastreamEventsForward(HttpEntityManager manager, UriTemplateMatch match) {
		var stream = match.BoundVariables["stream"];
		var evNum = match.BoundVariables["event"];
		var cnt = match.BoundVariables["count"];

		long eventNumber;
		int count;
		var embed = GetEmbedLevel(manager, match);

		if (stream.IsEmptyString() || SystemStreams.IsMetastream(stream))
			return SendBadRequest(manager, string.Format("Invalid stream name '{0}'", stream));
		if (evNum.IsEmptyString() || !long.TryParse(evNum, out eventNumber) || eventNumber < 0)
			return SendBadRequest(manager, string.Format("'{0}' is not valid event number", evNum));
		if (cnt.IsEmptyString() || !int.TryParse(cnt, out count) || count <= 0)
			return SendBadRequest(manager,
				string.Format("'{0}' is not valid count. Should be positive integer", cnt));
		bool resolveLinkTos;
		if (!GetResolveLinkTos(manager, out resolveLinkTos))
			return SendBadRequest(manager,
				string.Format("{0} header in wrong format.", SystemHeaders.ResolveLinkTos));
		if (!GetRequireLeader(manager, out var requireLeader))
			return SendBadRequest(manager,
				string.Format("{0} header in wrong format.", SystemHeaders.RequireLeader));
		TimeSpan? longPollTimeout;
		if (!GetLongPoll(manager, out longPollTimeout))
			return SendBadRequest(manager, string.Format("{0} header in wrong format.", SystemHeaders.LongPoll));
		var etag = GetETagStreamVersion(manager);

		GetStreamEventsForward(manager, SystemStreams.MetastreamOf(stream), eventNumber, count, resolveLinkTos,
			requireLeader, etag, longPollTimeout, embed);
		return new RequestParams((longPollTimeout ?? TimeSpan.Zero) + ESConsts.HttpTimeout);
	}

	// $ALL
	private void GetAllEventsBackward(HttpEntityManager manager, UriTemplateMatch match) {
		var pos = match.BoundVariables["position"];
		var cnt = match.BoundVariables["count"];

		TFPos position = TFPos.HeadOfTf;
		int count = AtomSpecs.FeedPageSize;
		var embed = GetEmbedLevel(manager, match);

		if (pos != null && pos != "head"
						&& (!TFPos.TryParse(pos, out position) || position.PreparePosition < 0 ||
							position.CommitPosition < 0)) {
			SendBadRequest(manager, string.Format("Invalid position argument: {0}", pos));
			return;
		}

		if (cnt.IsNotEmptyString() && (!int.TryParse(cnt, out count) || count <= 0)) {
			SendBadRequest(manager, string.Format("Invalid count argument: {0}", cnt));
			return;
		}

		if (!GetResolveLinkTos(manager, out var resolveLinkTos)) {
			SendBadRequest(manager, $"{SystemHeaders.ResolveLinkTos} header in wrong format.");
		}
		if (!GetRequireLeader(manager, out var requireLeader)) {
			SendBadRequest(manager, string.Format("{0} header in wrong format.", SystemHeaders.RequireLeader));
			return;
		}

		var envelope = new SendToHttpEnvelope(_networkSendQueue,
			manager,
			(args, msg) => Format.ReadAllEventsBackwardCompleted(args, msg, embed),
			(args, msg) => Configure.ReadAllEventsBackwardCompleted(args, msg, position == TFPos.HeadOfTf));
		var corrId = Guid.NewGuid();
		Publish(new ClientMessage.ReadAllEventsBackward(corrId, corrId, envelope,
			position.CommitPosition, position.PreparePosition, count, resolveLinkTos,
			requireLeader, GetETagTFPosition(manager), manager.User));
	}

	private void GetAllEventsBackwardFiltered(HttpEntityManager manager, UriTemplateMatch match) {
		var pos = match.BoundVariables["position"];
		var cnt = match.BoundVariables["count"];

		var (success, errorMessage) = GetFilterFromQueryString(match, true, out var filter);
		if (!success) {
			SendBadRequest(manager, errorMessage);
			return;
		}

		TFPos position = TFPos.HeadOfTf;
		int count = AtomSpecs.FeedPageSize;
		var embed = GetEmbedLevel(manager, match);

		if (pos != null && pos != "head"
						&& (!TFPos.TryParse(pos, out position) || position.PreparePosition < 0 ||
							position.CommitPosition < 0)) {
			SendBadRequest(manager, string.Format("Invalid position argument: {0}", pos));
			return;
		}

		if (cnt.IsNotEmptyString() && (!int.TryParse(cnt, out count) || count <= 0)) {
			SendBadRequest(manager, string.Format("Invalid count argument: {0}", cnt));
			return;
		}

		if (!GetRequireLeader(manager, out var requireLeader)) {
			SendBadRequest(manager, string.Format("{0} header in wrong format.", SystemHeaders.RequireLeader));
			return;
		}

		var envelope = new SendToHttpEnvelope(_networkSendQueue,
			manager,
			(args, msg) => Format.ReadAllEventsBackwardFilteredCompleted(args, msg, embed),
			(args, msg) => Configure.ReadAllEventsBackwardFilteredCompleted(args, msg, position == TFPos.HeadOfTf));
		var corrId = Guid.NewGuid();
		Publish(new ClientMessage.FilteredReadAllEventsBackward(corrId, corrId, envelope,
			position.CommitPosition, position.PreparePosition, count,
			requireLeader, true, count, GetETagTFPosition(manager), filter, manager.User));
	}



	private RequestParams GetAllEventsForward(HttpEntityManager manager, UriTemplateMatch match) {
		var pos = match.BoundVariables["position"];
		var cnt = match.BoundVariables["count"];

		TFPos position;
		int count;
		var embed = GetEmbedLevel(manager, match);

		if (!TFPos.TryParse(pos, out position) || position.PreparePosition < 0 || position.CommitPosition < 0)
			return SendBadRequest(manager, string.Format("Invalid position argument: {0}", pos));
		if (!int.TryParse(cnt, out count) || count <= 0)
			return SendBadRequest(manager, string.Format("Invalid count argument: {0}", cnt));
		if (!GetResolveLinkTos(manager, out var resolveLinkTos))
			return SendBadRequest(manager, $"{SystemHeaders.ResolveLinkTos} header in wrong format.");
		if (!GetRequireLeader(manager, out var requireLeader))
			return SendBadRequest(manager,
				string.Format("{0} header in wrong format.", SystemHeaders.RequireLeader));
		TimeSpan? longPollTimeout;
		if (!GetLongPoll(manager, out longPollTimeout))
			return SendBadRequest(manager, string.Format("{0} header in wrong format.", SystemHeaders.LongPoll));

		var envelope = new SendToHttpEnvelope(_networkSendQueue,
			manager,
			(args, msg) => Format.ReadAllEventsForwardCompleted(args, msg, embed),
			(args, msg) => Configure.ReadAllEventsForwardCompleted(args, msg, headOfTf: false));
		var corrId = Guid.NewGuid();
		Publish(new ClientMessage.ReadAllEventsForward(corrId, corrId, envelope,
			position.CommitPosition, position.PreparePosition, count, resolveLinkTos,
			requireLeader, GetETagTFPosition(manager), manager.User,
			replyOnExpired: false,
			longPollTimeout: longPollTimeout));
		return new RequestParams((longPollTimeout ?? TimeSpan.Zero) + ESConsts.HttpTimeout);
	}

	private RequestParams GetAllEventsForwardFiltered(HttpEntityManager manager, UriTemplateMatch match) {
		var pos = match.BoundVariables["position"];
		var cnt = match.BoundVariables["count"];

		var (success, errorMessage) = GetFilterFromQueryString(match, true, out var filter);
		if (!success) {
			return SendBadRequest(manager, errorMessage);
		}

		TFPos position;
		int count;
		var embed = GetEmbedLevel(manager, match);

		if (!TFPos.TryParse(pos, out position) || position.PreparePosition < 0 || position.CommitPosition < 0)
			return SendBadRequest(manager, string.Format("Invalid position argument: {0}", pos));
		if (!int.TryParse(cnt, out count) || count <= 0)
			return SendBadRequest(manager, string.Format("Invalid count argument: {0}", cnt));
		if (!GetRequireLeader(manager, out var requireLeader))
			return SendBadRequest(manager,
				string.Format("{0} header in wrong format.", SystemHeaders.RequireLeader));
		TimeSpan? longPollTimeout;
		if (!GetLongPoll(manager, out longPollTimeout))
			return SendBadRequest(manager, string.Format("{0} header in wrong format.", SystemHeaders.LongPoll));

		var envelope = new SendToHttpEnvelope(_networkSendQueue,
			manager,
			(args, msg) => Format.ReadAllEventsForwardFilteredCompleted(args, msg, embed),
			(args, msg) => Configure.ReadAllEventsForwardFilteredCompleted(args, msg, headOfTf: false));
		var corrId = Guid.NewGuid();
		Publish(new ClientMessage.FilteredReadAllEventsForward(corrId, corrId, envelope,
			position.CommitPosition, position.PreparePosition, count, true,
			requireLeader, 1000, GetETagTFPosition(manager), filter, manager.User,
			replyOnExpired: false,
			longPollTimeout: longPollTimeout));
		return new RequestParams((longPollTimeout ?? TimeSpan.Zero) + ESConsts.HttpTimeout);
	}

	// HELPERS
	private (bool Success, string ErrorMessage) GetFilterFromQueryString(UriTemplateMatch match, bool isAllStream, out IEventFilter filter) {
		var context = match.BoundVariables["context"];
		var type = match.BoundVariables["type"];
		var data = match.BoundVariables["data"];
		var excludeSystemEvents = match.BoundVariables["exclude-system-events"];

		if (excludeSystemEvents != null) {
			if (!bool.TryParse(excludeSystemEvents, out var parsedExcludeSystemEvents)) {
				filter = null;
				return (false, "exclude-sytem-events should have a value of true.");
			}

			if (parsedExcludeSystemEvents == false) {
				filter = null;
				return (false, "exclude-sytem-events should have a value of true.");
			}

			filter = EventFilter.Get(isAllStream, new Filter(
				Filter.Types.FilterContext.EventType,
				Filter.Types.FilterType.Regex,
				new[] { @"^[^\$].*" }
			));

			return (true, null);
		}

		var parsedFilterResult = EventFilter.TryParse(context, isAllStream, type, data, out filter);
		if (!parsedFilterResult.Success) {
			return (false, parsedFilterResult.Reason);
		}

		return (true, null);
	}

	private bool GetExpectedVersion(HttpEntityManager manager, out long expectedVersion) {
		var expVer = manager.HttpEntity.Request.GetHeaderValues(SystemHeaders.ExpectedVersion);
		if (StringValues.IsNullOrEmpty(expVer))
			expVer = manager.HttpEntity.Request.GetHeaderValues(SystemHeaders.LegacyExpectedVersion);

		if (StringValues.IsNullOrEmpty(expVer)) {
			expectedVersion = ExpectedVersion.Any;
			return true;
		}

		return long.TryParse(expVer, out expectedVersion) && expectedVersion >= ExpectedVersion.StreamExists;
	}

	private bool GetIncludedId(HttpEntityManager manager, out Guid includedId) {
		var id = manager.HttpEntity.Request.GetHeaderValues(SystemHeaders.EventId);
		if (StringValues.IsNullOrEmpty(id))
			id = manager.HttpEntity.Request.GetHeaderValues(SystemHeaders.LegacyEventId);

		if (StringValues.IsNullOrEmpty(id)) {
			includedId = Guid.Empty;
			return true;
		}

		return Guid.TryParse(id, out includedId) && includedId != Guid.Empty;
	}

	private bool GetIncludedType(HttpEntityManager manager, out string includedType) {
		var type = manager.HttpEntity.Request.GetHeaderValues(SystemHeaders.EventType);
		if (StringValues.IsNullOrEmpty(type))
			type = manager.HttpEntity.Request.GetHeaderValues(SystemHeaders.LegacyEventType);

		if (StringValues.IsNullOrEmpty(type)) {
			includedType = null;
			return true;
		}

		includedType = type;
		return true;
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

	private bool GetHardDelete(HttpEntityManager manager, out bool hardDelete) {
		hardDelete = false;
		var hardDel = manager.HttpEntity.Request.GetHeaderValues(SystemHeaders.HardDelete);
		if (StringValues.IsNullOrEmpty(hardDel))
			hardDel = manager.HttpEntity.Request.GetHeaderValues(SystemHeaders.LegacyHardDelete);

		if (StringValues.IsNullOrEmpty(hardDel))
			return true;
		if (string.Equals(hardDel, "True", StringComparison.OrdinalIgnoreCase)) {
			hardDelete = true;
			return true;
		}

		if (string.Equals(hardDel, "False", StringComparison.OrdinalIgnoreCase))
			return true;
		return false;
	}

	public void PostEntry(HttpEntityManager manager, long expectedVersion, bool requireLeader, string stream,
		Guid idIncluded, string typeIncluded) {
		//TODO GFY SHOULD WE MAKE THIS READ BYTE[] FOR RAW THEN CONVERT? AS OF NOW ITS ALL NO BOM UTF8
		manager.ReadRequestAsync(
			(man, body) => {
				var events = new Event[0];
				try {
					events = AutoEventConverter.SmartParse(body, manager.RequestCodec, idIncluded, typeIncluded);
				} catch (Exception ex) {
					SendBadRequest(manager, ex.Message);
					return;
				}

				if (events.IsEmpty()) {
					SendBadRequest(manager, "Write request body invalid.");
					return;
				}

				foreach (var e in events) {
					if (e.Data.Length + e.Metadata.Length > _maxAppendEventSize) {
						SendTooBig(manager, _maxAppendEventSize);
					}
				}

				var cts = CancellationTokenSource.CreateLinkedTokenSource(manager.HttpEntity.Context.RequestAborted);
				var envelope = new SendToHttpEnvelope(_networkSendQueue,
					manager,
					Format.WriteEventsCompleted,
					ConfigureResponse);
				var corrId = Guid.NewGuid();
				var msg = new ClientMessage.WriteEvents(corrId, corrId, envelope, requireLeader, stream,
					expectedVersion, events, manager.User,
					cancellationToken: manager.HttpEntity.Context.RequestAborted);
				cts.CancelAfter(_writeTimeout);
				Publish(msg);

				ResponseConfiguration ConfigureResponse(HttpResponseConfiguratorArgs a, Message m) {
					cts.Dispose();
					return Configure.WriteEventsCompleted(a, m, stream);
				}

			},
			e => Log.Debug("Error while reading request (POST entry): {e}.", e.Message));

	}

	private void GetStreamEvent(HttpEntityManager manager, string stream, long eventNumber,
		bool resolveLinkTos, bool requireLeader, EmbedLevel embed) {
		var envelope = new SendToHttpEnvelope(_networkSendQueue,
			manager,
			(args, message) => Format.EventEntry(args, message, embed),
			(args, message) => Configure.EventEntry(args, message, headEvent: eventNumber == -1));
		var corrId = Guid.NewGuid();
		Publish(new ClientMessage.ReadEvent(corrId, corrId, envelope, stream, eventNumber, resolveLinkTos,
			requireLeader, manager.User));
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

	private static long? GetETagTFPosition(HttpEntityManager manager) {
		var etag = manager.HttpEntity.Request.GetHeaderValues("If-None-Match");
		if (!StringValues.IsNullOrEmpty(etag)) {
			// etag format is version;contenttypehash
			var splitted = etag.ToString().Trim('\"').Split(ETagSeparatorArray);
			if (splitted.Length == 2) {
				var typeHash = manager.ResponseCodec.ContentType.GetHashCode()
					.ToString(CultureInfo.InvariantCulture);
				return splitted[1] == typeHash && long.TryParse(splitted[0], out var tfEofPosition)
					? (long?)tfEofPosition
					: null;
			}
		}

		return null;
	}

	private static EmbedLevel GetEmbedLevel(HttpEntityManager manager, UriTemplateMatch match,
		EmbedLevel htmlLevel = EmbedLevel.PrettyBody) {
		if (manager.ResponseCodec is IRichAtomCodec)
			return htmlLevel;
		var rawValue = match.BoundVariables["embed"] ?? string.Empty;
		switch (rawValue.ToLowerInvariant()) {
			case "content":
				return EmbedLevel.Content;
			case "rich":
				return EmbedLevel.Rich;
			case "body":
				return EmbedLevel.Body;
			case "pretty":
				return EmbedLevel.PrettyBody;
			case "tryharder":
				return EmbedLevel.TryHarder;
			default:
				return EmbedLevel.None;
		}
	}
}

internal class HtmlFeedCodec : ICodec, IRichAtomCodec {
	public string ContentType {
		get { return "text/html"; }
	}

	public Encoding Encoding {
		get { return Helper.UTF8NoBom; }
	}

	public bool HasEventIds {
		get { return false; }
	}

	public bool HasEventTypes {
		get { return false; }
	}

	public bool CanParse(MediaType format) {
		throw new NotImplementedException();
	}

	public bool SuitableForResponse(MediaType component) {
		return component.Type == "*"
			   || (string.Equals(component.Type, "text", StringComparison.OrdinalIgnoreCase)
				   && (component.Subtype == "*" ||
					   string.Equals(component.Subtype, "html", StringComparison.OrdinalIgnoreCase)));
	}

	public T From<T>(string text) {
		throw new NotImplementedException();
	}

	public string To<T>(T value) {
		return @"
            <!DOCTYPE html>
            <html>
            <head>
            </head>
            <body>
            <script>
                var data = " + JsonConvert.SerializeObject(value, Formatting.Indented, JsonCodec.ToSettings) + @";
                var newLocation = '/web/index.html#/streams/' + data.streamId" + @"
                if('positionEventNumber' in data){
                    newLocation = newLocation + '/' + data.positionEventNumber;
                }
                window.location.replace(newLocation);
            </script>
            </body>
            </html>
            ";
	}
}

interface IRichAtomCodec {
}
