// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Text;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Monitoring;
using EventStore.Core.Services.Transport.Http.Controllers;
using ContentType = EventStore.Transport.Http.ContentType;
using HttpStatusCode = EventStore.Transport.Http.HttpStatusCode;
using OperationResult = EventStore.Core.Messages.OperationResult;
using ReadStreamResult = EventStore.Core.Data.ReadStreamResult;

namespace EventStore.Core.Services.Transport.Http;

public static class Configure {
	private const int MaxPossibleAge = 31536000;
	public static bool DisableHttpCaching = false;

	public static ResponseConfiguration Ok(string contentType) => new(HttpStatusCode.OK, "OK", contentType);

	public static ResponseConfiguration Ok(string contentType,
		Encoding encoding,
		string etag,
		int? cacheSeconds,
		bool isCachePublic,
		params KeyValuePair<string, string>[] headers) {
		var headrs = new List<KeyValuePair<string, string>>(headers);
		if (DisableHttpCaching) {
			headrs.Add(new("Cache-Control", "max-age=0, no-cache, must-revalidate"));
		} else {
			headrs.Add(new(
				"Cache-Control",
				cacheSeconds.HasValue
					? $"max-age={cacheSeconds}, {(isCachePublic ? "public" : "private")}"
					: "max-age=0, no-cache, must-revalidate")
			);
		}

		headrs.Add(new("Vary", "Accept"));
		if (etag.IsNotEmptyString())
			headrs.Add(new("ETag", $"\"{etag}\""));
		return new(HttpStatusCode.OK, "OK", contentType, encoding, headrs);
	}

	public static ResponseConfiguration TemporaryRedirect(Uri originalUrl, string targetHost, int targetPort) {
		var srcBase = new Uri($"{originalUrl.Scheme}://{originalUrl.Host}:{originalUrl.Port}/", UriKind.Absolute);
		var targetBase = new Uri($"{originalUrl.Scheme}://{targetHost}:{targetPort}/", UriKind.Absolute);
		var forwardUri = new Uri(targetBase, srcBase.MakeRelativeUri(originalUrl));
		return new(
			HttpStatusCode.TemporaryRedirect,
			"Temporary Redirect",
			ContentType.PlainText,
			new KeyValuePair<string, string>("Location", forwardUri.ToString())
		);
	}

	public static ResponseConfiguration DenyRequestBecauseReadOnly(Uri originalUrl, string targetHost, int targetPort) {
		var srcBase = new Uri($"{originalUrl.Scheme}://{originalUrl.Host}:{originalUrl.Port}/", UriKind.Absolute);
		var targetBase = new Uri($"{originalUrl.Scheme}://{targetHost}:{targetPort}/", UriKind.Absolute);
		var forwardUri = new Uri(targetBase, srcBase.MakeRelativeUri(originalUrl));
		return new(
			HttpStatusCode.InternalServerError,
			"Operation Not Supported on Read Only Replica",
			ContentType.PlainText,
			new KeyValuePair<string, string>("Location", forwardUri.ToString())
		);
	}

	public static ResponseConfiguration NotFound() => new(HttpStatusCode.NotFound, "Not Found", ContentType.PlainText);

	public static ResponseConfiguration NotFound(string etag, int? cacheSeconds, bool isCachePublic, string contentType) {
		var headers = new List<KeyValuePair<string, string>> {
			new(
				"Cache-Control",
				cacheSeconds.HasValue
					? $"max-age={cacheSeconds}, {(isCachePublic ? "public" : "private")}"
					: "max-age=0, no-cache, must-revalidate"
			),
			new("Vary", "Accept")
		};
		if (etag.IsNotEmptyString())
			headers.Add(new("ETag", $"\"{etag}\""));
		return new(HttpStatusCode.NotFound, "Not Found", contentType, Helper.UTF8NoBom, headers);
	}

	public static ResponseConfiguration Gone(string description = null) {
		return new(HttpStatusCode.Gone, description ?? "Deleted", ContentType.PlainText);
	}

	public static ResponseConfiguration NotModified() {
		return new(HttpStatusCode.NotModified, "Not Modified", ContentType.PlainText);
	}

	public static ResponseConfiguration BadRequest(string description = null) {
		return new(HttpStatusCode.BadRequest, description ?? "Bad Request", ContentType.PlainText);
	}

	public static ResponseConfiguration InternalServerError(string description = null) {
		return new(HttpStatusCode.InternalServerError, description ?? "Internal Server Error", ContentType.PlainText);
	}

	public static ResponseConfiguration ServiceUnavailable(string description = null) {
		return new(HttpStatusCode.ServiceUnavailable, description ?? "Service Unavailable", ContentType.PlainText);
	}

	public static ResponseConfiguration NotImplemented(string description = null) {
		return new(HttpStatusCode.NotImplemented, description ?? "Not Implemented", ContentType.PlainText);
	}

	public static ResponseConfiguration Unauthorized(string description = null) {
		return new(HttpStatusCode.Unauthorized, description ?? "Unauthorized", ContentType.PlainText);
	}

	public static ResponseConfiguration EventEntry(HttpResponseConfiguratorArgs entity, Message message, bool headEvent) {
		switch (message) {
			case ClientMessage.ReadEventCompleted msg:
				switch (msg.Result) {
					case ReadEventResult.Success:
						var codec = entity.ResponseCodec;
						if (msg.Record.Event == null && msg.Record.Link != null) {
							return NotFound();
						}

						if (!headEvent) return Ok(codec.ContentType, codec.Encoding, null, MaxPossibleAge, msg.IsCachePublic);

						var etag = msg.Record.OriginalEvent != null
							? GetPositionETag(msg.Record.OriginalEventNumber, codec.ContentType)
							: string.Empty;
						var cacheSeconds = GetCacheSeconds(msg.StreamMetadata);
						return Ok(codec.ContentType, codec.Encoding, etag, cacheSeconds, msg.IsCachePublic);

					case ReadEventResult.NotFound:
					case ReadEventResult.NoStream:
						return NotFound();
					case ReadEventResult.StreamDeleted:
						return Gone();
					case ReadEventResult.Error:
						return InternalServerError(msg.Error);
					case ReadEventResult.AccessDenied:
						return Unauthorized();
					default:
						throw new ArgumentOutOfRangeException();
				}
			case ClientMessage.NotHandled notHandled:
				return HandleNotHandled(entity.RequestedUrl, notHandled);
			default:
				return InternalServerError();
		}
	}

	public static ResponseConfiguration EventMetadata(HttpResponseConfiguratorArgs entity) {
		return NotImplemented();
	}

	public static ResponseConfiguration GetStreamEventsBackward(HttpResponseConfiguratorArgs entity, Message message, bool headOfStream) {
		switch (message) {
			case ClientMessage.ReadStreamEventsBackwardCompleted msg:
				switch (msg.Result) {
					case ReadStreamResult.Success:
						var codec = entity.ResponseCodec;
						if (msg.LastEventNumber >= msg.FromEventNumber && !headOfStream)
							return Ok(codec.ContentType, codec.Encoding, null, MaxPossibleAge, msg.IsCachePublic);
						var etag = GetPositionETag(msg.LastEventNumber, codec.ContentType);
						var cacheSeconds = GetCacheSeconds(msg.StreamMetadata);
						return Ok(codec.ContentType, codec.Encoding, etag, cacheSeconds, msg.IsCachePublic);
					case ReadStreamResult.NoStream:
						return NotFound();
					case ReadStreamResult.StreamDeleted:
						return Gone();
					case ReadStreamResult.NotModified:
						return NotModified();
					case ReadStreamResult.Error:
						return InternalServerError(msg.Error);
					case ReadStreamResult.AccessDenied:
						return Unauthorized();
					default:
						throw new ArgumentOutOfRangeException();
				}
			case ClientMessage.NotHandled notHandled:
				return HandleNotHandled(entity.RequestedUrl, notHandled);
			default:
				return InternalServerError();
		}
	}

	public static ResponseConfiguration
		GetStreamEventsForward(HttpResponseConfiguratorArgs entity, Message message) {
		switch (message) {
			case ClientMessage.ReadStreamEventsForwardCompleted msg:
				switch (msg.Result) {
					case ReadStreamResult.Success:
						var codec = entity.ResponseCodec;
						var etag = GetPositionETag(msg.LastEventNumber, codec.ContentType);
						var cacheSeconds = GetCacheSeconds(msg.StreamMetadata);
						return msg.LastEventNumber + 1 >= msg.FromEventNumber + msg.MaxCount
							? Ok(codec.ContentType, codec.Encoding, null, MaxPossibleAge, msg.IsCachePublic)
							: Ok(codec.ContentType, codec.Encoding, etag, cacheSeconds, msg.IsCachePublic);
					case ReadStreamResult.NoStream:
						return NotFound();
					case ReadStreamResult.StreamDeleted:
						return Gone();
					case ReadStreamResult.NotModified:
						return NotModified();
					case ReadStreamResult.Error:
						return InternalServerError(msg.Error);
					case ReadStreamResult.AccessDenied:
						return Unauthorized();
					default:
						throw new ArgumentOutOfRangeException();
				}
			case ClientMessage.NotHandled notHandled:
				return HandleNotHandled(entity.RequestedUrl, notHandled);
			default:
				return InternalServerError();
		}
	}

	public static ResponseConfiguration ReadAllEventsBackwardCompleted(HttpResponseConfiguratorArgs entity, Message message, bool headOfTf) {
		switch (message) {
			case ClientMessage.ReadAllEventsBackwardCompleted msg:
				switch (msg.Result) {
					case ReadAllResult.Success:
						var codec = entity.ResponseCodec;
						if (!headOfTf && msg.CurrentPos.CommitPosition <= msg.TfLastCommitPosition)
							return Ok(codec.ContentType, codec.Encoding, null, MaxPossibleAge, msg.IsCachePublic);
						var etag = GetPositionETag(msg.TfLastCommitPosition, codec.ContentType);
						var cacheSeconds = GetCacheSeconds(msg.StreamMetadata);
						return Ok(codec.ContentType, codec.Encoding, etag, cacheSeconds, msg.IsCachePublic);
					case ReadAllResult.NotModified:
						return NotModified();
					case ReadAllResult.Error:
					case ReadAllResult.InvalidPosition:
						return InternalServerError(msg.Error);
					case ReadAllResult.AccessDenied:
						return Unauthorized();
					default:
						throw new ArgumentOutOfRangeException();
				}
			case ClientMessage.NotHandled notHandled:
				return HandleNotHandled(entity.RequestedUrl, notHandled);
			default:
				return InternalServerError();
		}
	}

	public static ResponseConfiguration ReadAllEventsBackwardFilteredCompleted(HttpResponseConfiguratorArgs entity, Message message, bool headOfTf) {
		switch (message) {
			case ClientMessage.FilteredReadAllEventsBackwardCompleted msg:
				switch (msg.Result) {
					case FilteredReadAllResult.Success:
						var codec = entity.ResponseCodec;
						if (!headOfTf && msg.CurrentPos.CommitPosition <= msg.TfLastCommitPosition)
							return Ok(codec.ContentType, codec.Encoding, null, MaxPossibleAge, msg.IsCachePublic);
						var etag = GetPositionETag(msg.TfLastCommitPosition, codec.ContentType);
						var cacheSeconds = GetCacheSeconds(msg.StreamMetadata);
						return Ok(codec.ContentType, codec.Encoding, etag, cacheSeconds, msg.IsCachePublic);
					case FilteredReadAllResult.NotModified:
						return NotModified();
					case FilteredReadAllResult.Error:
					case FilteredReadAllResult.InvalidPosition:
						return InternalServerError(msg.Error);
					case FilteredReadAllResult.AccessDenied:
						return Unauthorized();
					default:
						throw new ArgumentOutOfRangeException();
				}
			case ClientMessage.NotHandled notHandled:
				return HandleNotHandled(entity.RequestedUrl, notHandled);
			default:
				return InternalServerError();
		}
	}

	public static ResponseConfiguration ReadAllEventsForwardCompleted(HttpResponseConfiguratorArgs entity, Message message, bool headOfTf) {
		switch (message) {
			case ClientMessage.ReadAllEventsForwardCompleted msg:
				switch (msg.Result) {
					case ReadAllResult.Success:
						var codec = entity.ResponseCodec;
						if (!headOfTf && msg.Events.Count == msg.MaxCount)
							return Ok(codec.ContentType, codec.Encoding, null, MaxPossibleAge, msg.IsCachePublic);
						var etag = GetPositionETag(msg.TfLastCommitPosition, codec.ContentType);
						var cacheSeconds = GetCacheSeconds(msg.StreamMetadata);
						return Ok(codec.ContentType, codec.Encoding, etag, cacheSeconds, msg.IsCachePublic);
					case ReadAllResult.NotModified:
						return NotModified();
					case ReadAllResult.Error:
					case ReadAllResult.InvalidPosition:
						return InternalServerError(msg.Error);
					case ReadAllResult.AccessDenied:
						return Unauthorized();
					default:
						throw new ArgumentOutOfRangeException();
				}
			case ClientMessage.NotHandled notHandled:
				return HandleNotHandled(entity.RequestedUrl, notHandled);
			default:
				return InternalServerError();
		}
	}

	public static ResponseConfiguration ReadAllEventsForwardFilteredCompleted(HttpResponseConfiguratorArgs entity, Message message, bool headOfTf) {
		switch (message) {
			case ClientMessage.FilteredReadAllEventsForwardCompleted msg:
				switch (msg.Result) {
					case FilteredReadAllResult.Success:
						var codec = entity.ResponseCodec;
						if (!headOfTf && msg.Events.Count == msg.MaxCount)
							return Ok(codec.ContentType, codec.Encoding, null, MaxPossibleAge, msg.IsCachePublic);
						var etag = GetPositionETag(msg.TfLastCommitPosition, codec.ContentType);
						var cacheSeconds = GetCacheSeconds(msg.StreamMetadata);
						return Ok(codec.ContentType, codec.Encoding, etag, cacheSeconds, msg.IsCachePublic);
					case FilteredReadAllResult.NotModified:
						return NotModified();
					case FilteredReadAllResult.Error:
					case FilteredReadAllResult.InvalidPosition:
						return InternalServerError(msg.Error);
					case FilteredReadAllResult.AccessDenied:
						return Unauthorized();
					default:
						throw new ArgumentOutOfRangeException();
				}
			case ClientMessage.NotHandled notHandled:
				return HandleNotHandled(entity.RequestedUrl, notHandled);
			default:
				return InternalServerError();
		}
	}

	public static string GetPositionETag(long position, string contentType) {
		return $"{position}{AtomController.ETagSeparator}{contentType.GetHashCode()}";
	}

	private static int? GetCacheSeconds(StreamMetadata metadata) {
		return metadata is { CacheControl: not null }
			? (int)metadata.CacheControl.Value.TotalSeconds
			: null;
	}

	public static ResponseConfiguration WriteEventsCompleted(HttpResponseConfiguratorArgs entity, Message message,
		string eventStreamId) {
		switch (message) {
			case ClientMessage.WriteEventsCompleted msg:
				switch (msg.Result) {
					case OperationResult.Success:
						var location = HostName.Combine(entity.ResponseUrl, "/streams/{0}/{1}", Uri.EscapeDataString(eventStreamId), msg.FirstEventNumber);
						return new(HttpStatusCode.Created, "Created", ContentType.PlainText, new KeyValuePair<string, string>("Location", location));
					case OperationResult.PrepareTimeout:
					case OperationResult.CommitTimeout:
					case OperationResult.ForwardTimeout:
						return InternalServerError("Write timeout");
					case OperationResult.WrongExpectedVersion:
						return new(HttpStatusCode.BadRequest, "Wrong expected EventNumber",
							ContentType.PlainText,
							new KeyValuePair<string, string>(SystemHeaders.CurrentVersion, msg.CurrentVersion.ToString()),
							new KeyValuePair<string, string>(SystemHeaders.LegacyCurrentVersion, msg.CurrentVersion.ToString()));
					case OperationResult.StreamDeleted:
						return Gone("Stream deleted");
					case OperationResult.InvalidTransaction:
						return InternalServerError("Invalid transaction");
					case OperationResult.AccessDenied:
						return Unauthorized();
					default:
						throw new ArgumentOutOfRangeException();
				}
			case ClientMessage.NotHandled notHandled:
				return HandleNotHandled(entity.RequestedUrl, notHandled);
			default:
				return InternalServerError();
		}
	}

	public static ResponseConfiguration
		DeleteStreamCompleted(HttpResponseConfiguratorArgs entity, Message message) {
		switch (message) {
			case ClientMessage.DeleteStreamCompleted msg:
				switch (msg.Result) {
					case OperationResult.Success:
						return new(HttpStatusCode.NoContent, "Stream deleted", ContentType.PlainText);
					case OperationResult.PrepareTimeout:
					case OperationResult.CommitTimeout:
					case OperationResult.ForwardTimeout:
						return InternalServerError("Delete timeout");
					case OperationResult.WrongExpectedVersion:
						return new(HttpStatusCode.BadRequest, "Wrong expected EventNumber",
							ContentType.PlainText,
							new KeyValuePair<string, string>(SystemHeaders.CurrentVersion, msg.CurrentVersion.ToString()),
							new KeyValuePair<string, string>(SystemHeaders.LegacyCurrentVersion, msg.CurrentVersion.ToString()));
					case OperationResult.StreamDeleted:
						return Gone("Stream deleted");
					case OperationResult.InvalidTransaction:
						return InternalServerError("Invalid transaction");
					case OperationResult.AccessDenied:
						return Unauthorized();
					default:
						throw new ArgumentOutOfRangeException();
				}
			case ClientMessage.NotHandled notHandled:
				return HandleNotHandled(entity.RequestedUrl, notHandled);
			default:
				return InternalServerError();
		}
	}

	private static ResponseConfiguration HandleNotHandled(Uri requestedUri, ClientMessage.NotHandled notHandled) {
		switch (notHandled.Reason) {
			case ClientMessage.NotHandled.Types.NotHandledReason.NotReady:
				return ServiceUnavailable("Server Is Not Ready");
			case ClientMessage.NotHandled.Types.NotHandledReason.TooBusy:
				return ServiceUnavailable("Server Is Too Busy");
			case ClientMessage.NotHandled.Types.NotHandledReason.NotLeader: {
				var leaderInfo = notHandled.LeaderInfo;
				return leaderInfo == null
					? InternalServerError("No leader info available in response")
					: TemporaryRedirect(requestedUri, leaderInfo.Http.GetHost(), leaderInfo.Http.GetPort());
			}
			case ClientMessage.NotHandled.Types.NotHandledReason.IsReadOnly: {
				var leaderInfo = notHandled.LeaderInfo;
				return leaderInfo == null
					? InternalServerError("No leader info available in response")
					: DenyRequestBecauseReadOnly(requestedUri, leaderInfo.Http.GetHost(), leaderInfo.Http.GetPort());
			}
			default:
				return InternalServerError($"Unknown not handled reason: {notHandled.Reason}");
		}
	}

	public static ResponseConfiguration GetFreshStatsCompleted(HttpResponseConfiguratorArgs entity, Message message) {
		if (message is not MonitoringMessage.GetFreshStatsCompleted completed)
			return InternalServerError();

		var cacheSeconds = (int)MonitoringService.MemoizePeriod.TotalSeconds;
		return completed.Success
			? Ok(entity.ResponseCodec.ContentType, Helper.UTF8NoBom, null, cacheSeconds, isCachePublic: true)
			: NotFound();
	}

	public static ResponseConfiguration GetReplicationStatsCompleted(HttpResponseConfiguratorArgs entity, Message message) {
		if (message is not ReplicationMessage.GetReplicationStatsCompleted)
			return InternalServerError();

		var cacheSeconds = (int)MonitoringService.MemoizePeriod.TotalSeconds;
		return Ok(entity.ResponseCodec.ContentType, Helper.UTF8NoBom, null, cacheSeconds, isCachePublic: true);
	}

	public static ResponseConfiguration GetFreshTcpConnectionStatsCompleted(HttpResponseConfiguratorArgs entity, Message message) {
		if (message is not MonitoringMessage.GetFreshTcpConnectionStatsCompleted)
			return InternalServerError();

		var cacheSeconds = (int)MonitoringService.MemoizePeriod.TotalSeconds;
		return Ok(entity.ResponseCodec.ContentType, Helper.UTF8NoBom, null, cacheSeconds, isCachePublic: true);
	}
}
