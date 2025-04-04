using System;
using System.Collections.Generic;
using System.Text;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Monitoring;
using EventStore.Core.Services.Transport.Http.Controllers;
using HttpStatusCode = EventStore.Transport.Http.HttpStatusCode;
using OperationResult = EventStore.Core.Messages.OperationResult;
using ReadStreamResult = EventStore.Core.Data.ReadStreamResult;

namespace EventStore.Core.Services.Transport.Http {
	public static class Configure {
		private const int MaxPossibleAge = 31536000;
		public static bool DisableHTTPCaching = false;

		public static ResponseConfiguration Ok(string contentType) {
			return new ResponseConfiguration(HttpStatusCode.OK, "OK", contentType, Helper.UTF8NoBom);
		}

		public static ResponseConfiguration Ok(string contentType,
			Encoding encoding,
			string etag,
			int? cacheSeconds,
			bool isCachePublic,
			params KeyValuePair<string, string>[] headers) {
			var headrs = new List<KeyValuePair<string, string>>(headers);
			if (DisableHTTPCaching) {
				headrs.Add(new KeyValuePair<string, string>(
					"Cache-Control",
					"max-age=0, no-cache, must-revalidate"));
			} else {
				headrs.Add(new KeyValuePair<string, string>(
					"Cache-Control",
					cacheSeconds.HasValue
						? string.Format("max-age={0}, {1}", cacheSeconds, isCachePublic ? "public" : "private")
						: "max-age=0, no-cache, must-revalidate"));
			}

			headrs.Add(new KeyValuePair<string, string>("Vary", "Accept"));
			if (etag.IsNotEmptyString())
				headrs.Add(new KeyValuePair<string, string>("ETag", string.Format("\"{0}\"", etag)));
			return new ResponseConfiguration(HttpStatusCode.OK, "OK", contentType, encoding, headrs);
		}

		public static ResponseConfiguration TemporaryRedirect(Uri originalUrl, string targetHost, int targetPort) {
			var srcBase =
				new Uri(string.Format("{0}://{1}:{2}/", originalUrl.Scheme, originalUrl.Host, originalUrl.Port),
					UriKind.Absolute);
			var targetBase = new Uri(string.Format("{0}://{1}:{2}/", originalUrl.Scheme, targetHost, targetPort),
				UriKind.Absolute);
			var forwardUri = new Uri(targetBase, srcBase.MakeRelativeUri(originalUrl));
			return new ResponseConfiguration(HttpStatusCode.TemporaryRedirect, "Temporary Redirect", "text/plain",
				Helper.UTF8NoBom,
				new KeyValuePair<string, string>("Location", forwardUri.ToString()));
		}

		public static ResponseConfiguration DenyRequestBecauseReadOnly(Uri originalUrl, string targetHost, int targetPort) {
			var srcBase =
				new Uri(string.Format("{0}://{1}:{2}/", originalUrl.Scheme, originalUrl.Host, originalUrl.Port),
					UriKind.Absolute);
			var targetBase = new Uri(string.Format("{0}://{1}:{2}/", originalUrl.Scheme, targetHost, targetPort),
				UriKind.Absolute);
			var forwardUri = new Uri(targetBase, srcBase.MakeRelativeUri(originalUrl));
			return new ResponseConfiguration(HttpStatusCode.InternalServerError,
				"Operation Not Supported on Read Only Replica", "text/plain",
				Helper.UTF8NoBom,
				new KeyValuePair<string, string>("Location", forwardUri.ToString()));
		}

		public static ResponseConfiguration NotFound() {
			return new ResponseConfiguration(HttpStatusCode.NotFound, "Not Found", "text/plain", Helper.UTF8NoBom);
		}

		public static ResponseConfiguration NotFound(string etag, int? cacheSeconds, bool isCachePublic,
			string contentType) {
			var headrs = new List<KeyValuePair<string, string>>();
			headrs.Add(new KeyValuePair<string, string>(
				"Cache-Control",
				cacheSeconds.HasValue
					? string.Format("max-age={0}, {1}", cacheSeconds, isCachePublic ? "public" : "private")
					: "max-age=0, no-cache, must-revalidate"));
			headrs.Add(new KeyValuePair<string, string>("Vary", "Accept"));
			if (etag.IsNotEmptyString())
				headrs.Add(new KeyValuePair<string, string>("ETag", string.Format("\"{0}\"", etag)));
			return new ResponseConfiguration(HttpStatusCode.NotFound, "Not Found", contentType, Helper.UTF8NoBom,
				headrs);
		}

		public static ResponseConfiguration Gone(string description = null) {
			return new ResponseConfiguration(HttpStatusCode.Gone, description ?? "Deleted", "text/plain",
				Helper.UTF8NoBom);
		}

		public static ResponseConfiguration NotModified() {
			return new ResponseConfiguration(HttpStatusCode.NotModified, "Not Modified", "text/plain",
				Helper.UTF8NoBom);
		}

		public static ResponseConfiguration BadRequest(string description = null) {
			return new ResponseConfiguration(HttpStatusCode.BadRequest, description ?? "Bad Request", "text/plain",
				Helper.UTF8NoBom);
		}

		public static ResponseConfiguration InternalServerError(string description = null) {
			return new ResponseConfiguration(HttpStatusCode.InternalServerError, description ?? "Internal Server Error",
				"text/plain", Helper.UTF8NoBom);
		}

		public static ResponseConfiguration ServiceUnavailable(string description = null) {
			return new ResponseConfiguration(HttpStatusCode.ServiceUnavailable, description ?? "Service Unavailable",
				"text/plain", Helper.UTF8NoBom);
		}

		public static ResponseConfiguration NotImplemented(string description = null) {
			return new ResponseConfiguration(HttpStatusCode.NotImplemented, description ?? "Not Implemented",
				"text/plain", Helper.UTF8NoBom);
		}

		public static ResponseConfiguration Unauthorized(string description = null) {
			return new ResponseConfiguration(HttpStatusCode.Unauthorized, description ?? "Unauthorized", "text/plain",
				Helper.UTF8NoBom);
		}

		public static ResponseConfiguration EventEntry(HttpResponseConfiguratorArgs entity, Message message,
			bool headEvent) {
			var msg = message as ClientMessage.ReadEventCompleted;
			if (msg != null) {
				switch (msg.Result) {
					case ReadEventResult.Success:
						var codec = entity.ResponseCodec;
						if (msg.Record.Event == null && msg.Record.Link != null) {
							return NotFound();
						}

						if (headEvent) {
							var etag = msg.Record.OriginalEvent != null
								? GetPositionETag(msg.Record.OriginalEventNumber, codec.ContentType)
								: String.Empty;
							var cacheSeconds = GetCacheSeconds(msg.StreamMetadata);
							return Ok(codec.ContentType, codec.Encoding, etag, cacheSeconds, msg.IsCachePublic);
						}

						return Ok(codec.ContentType, codec.Encoding, null, MaxPossibleAge, msg.IsCachePublic);
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
			}

			var notHandled = message as ClientMessage.NotHandled;
			if (notHandled != null)
				return HandleNotHandled(entity.RequestedUrl, notHandled);
			return InternalServerError();
		}

		public static ResponseConfiguration EventMetadata(HttpResponseConfiguratorArgs entity) {
			return NotImplemented();
		}

		public static ResponseConfiguration GetStreamEventsBackward(HttpResponseConfiguratorArgs entity,
			Message message, bool headOfStream) {
			var msg = message as ClientMessage.ReadStreamEventsBackwardCompleted;
			if (msg != null) {
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
			}

			var notHandled = message as ClientMessage.NotHandled;
			if (notHandled != null)
				return HandleNotHandled(entity.RequestedUrl, notHandled);
			return InternalServerError();
		}

		public static ResponseConfiguration
			GetStreamEventsForward(HttpResponseConfiguratorArgs entity, Message message) {
			var msg = message as ClientMessage.ReadStreamEventsForwardCompleted;
			if (msg != null) {
				switch (msg.Result) {
					case ReadStreamResult.Success:
						var codec = entity.ResponseCodec;
						var etag = GetPositionETag(msg.LastEventNumber, codec.ContentType);
						var cacheSeconds = GetCacheSeconds(msg.StreamMetadata);
						if (msg.LastEventNumber + 1 >= msg.FromEventNumber + msg.MaxCount)
							return Ok(codec.ContentType, codec.Encoding, null, MaxPossibleAge, msg.IsCachePublic);
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
			}

			var notHandled = message as ClientMessage.NotHandled;
			if (notHandled != null)
				return HandleNotHandled(entity.RequestedUrl, notHandled);
			return InternalServerError();
		}

		public static ResponseConfiguration ReadAllEventsBackwardCompleted(HttpResponseConfiguratorArgs entity,
			Message message, bool headOfTf) {
			var msg = message as ClientMessage.ReadAllEventsBackwardCompleted;
			if (msg != null) {
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
						return InternalServerError(msg.Error);
					case ReadAllResult.AccessDenied:
						return Unauthorized();
					default:
						throw new ArgumentOutOfRangeException();
				}
			}

			var notHandled = message as ClientMessage.NotHandled;
			if (notHandled != null)
				return HandleNotHandled(entity.RequestedUrl, notHandled);
			return InternalServerError();
		}

		public static ResponseConfiguration ReadAllEventsBackwardFilteredCompleted(HttpResponseConfiguratorArgs entity,
			Message message, bool headOfTf) {
			var msg = message as ClientMessage.FilteredReadAllEventsBackwardCompleted;
			if (msg != null) {
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
						return InternalServerError(msg.Error);
					case FilteredReadAllResult.AccessDenied:
						return Unauthorized();
					default:
						throw new ArgumentOutOfRangeException();
				}
			}

			var notHandled = message as ClientMessage.NotHandled;
			if (notHandled != null)
				return HandleNotHandled(entity.RequestedUrl, notHandled);
			return InternalServerError();
		}

		public static ResponseConfiguration ReadAllEventsForwardCompleted(HttpResponseConfiguratorArgs entity,
			Message message, bool headOfTf) {
			var msg = message as ClientMessage.ReadAllEventsForwardCompleted;
			if (msg != null) {
				switch (msg.Result) {
					case ReadAllResult.Success:
						var codec = entity.ResponseCodec;
						if (!headOfTf && msg.Events.Length == msg.MaxCount)
							return Ok(codec.ContentType, codec.Encoding, null, MaxPossibleAge, msg.IsCachePublic);
						var etag = GetPositionETag(msg.TfLastCommitPosition, codec.ContentType);
						var cacheSeconds = GetCacheSeconds(msg.StreamMetadata);
						return Ok(codec.ContentType, codec.Encoding, etag, cacheSeconds, msg.IsCachePublic);
					case ReadAllResult.NotModified:
						return NotModified();
					case ReadAllResult.Error:
						return InternalServerError(msg.Error);
					case ReadAllResult.AccessDenied:
						return Unauthorized();
					default:
						throw new ArgumentOutOfRangeException();
				}
			}

			var notHandled = message as ClientMessage.NotHandled;
			if (notHandled != null)
				return HandleNotHandled(entity.RequestedUrl, notHandled);
			return InternalServerError();
		}

		public static ResponseConfiguration ReadAllEventsForwardFilteredCompleted(HttpResponseConfiguratorArgs entity,
			Message message, bool headOfTf) {
			var msg = message as ClientMessage.FilteredReadAllEventsForwardCompleted;
			if (msg != null) {
				switch (msg.Result) {
					case FilteredReadAllResult.Success:
						var codec = entity.ResponseCodec;
						if (!headOfTf && msg.Events.Length == msg.MaxCount)
							return Ok(codec.ContentType, codec.Encoding, null, MaxPossibleAge, msg.IsCachePublic);
						var etag = GetPositionETag(msg.TfLastCommitPosition, codec.ContentType);
						var cacheSeconds = GetCacheSeconds(msg.StreamMetadata);
						return Ok(codec.ContentType, codec.Encoding, etag, cacheSeconds, msg.IsCachePublic);
					case FilteredReadAllResult.NotModified:
						return NotModified();
					case FilteredReadAllResult.Error:
						return InternalServerError(msg.Error);
					case FilteredReadAllResult.AccessDenied:
						return Unauthorized();
					default:
						throw new ArgumentOutOfRangeException();
				}
			}

			var notHandled = message as ClientMessage.NotHandled;
			if (notHandled != null)
				return HandleNotHandled(entity.RequestedUrl, notHandled);
			return InternalServerError();
		}

		public static string GetPositionETag(long position, string contentType) {
			return string.Format("{0}{1}{2}", position, AtomController.ETagSeparator, contentType.GetHashCode());
		}

		private static int? GetCacheSeconds(StreamMetadata metadata) {
			return metadata != null && metadata.CacheControl.HasValue
				? (int)metadata.CacheControl.Value.TotalSeconds
				: (int?)null;
		}

		public static ResponseConfiguration WriteEventsCompleted(HttpResponseConfiguratorArgs entity, Message message,
			string eventStreamId) {
			var msg = message as ClientMessage.WriteEventsCompleted;
			if (msg != null) {
				switch (msg.Result) {
					case OperationResult.Success:
						var location = HostName.Combine(entity.ResponseUrl, "/streams/{0}/{1}",
							Uri.EscapeDataString(eventStreamId), msg.FirstEventNumber);
						return new ResponseConfiguration(HttpStatusCode.Created, "Created", "text/plain",
							Helper.UTF8NoBom,
							new KeyValuePair<string, string>("Location", location));
					case OperationResult.PrepareTimeout:
					case OperationResult.CommitTimeout:
					case OperationResult.ForwardTimeout:
						return InternalServerError("Write timeout");
					case OperationResult.WrongExpectedVersion:
						return new ResponseConfiguration(HttpStatusCode.BadRequest, "Wrong expected EventNumber",
							"text/plain", Helper.UTF8NoBom,
							new KeyValuePair<string, string>(SystemHeaders.CurrentVersion,
								msg.CurrentVersion.ToString()));
					case OperationResult.StreamDeleted:
						return Gone("Stream deleted");
					case OperationResult.InvalidTransaction:
						return InternalServerError("Invalid transaction");
					case OperationResult.AccessDenied:
						return Unauthorized();
					default:
						throw new ArgumentOutOfRangeException();
				}
			}

			var notHandled = message as ClientMessage.NotHandled;
			if (notHandled != null)
				return HandleNotHandled(entity.RequestedUrl, notHandled);
			return InternalServerError();
		}

		public static ResponseConfiguration
			DeleteStreamCompleted(HttpResponseConfiguratorArgs entity, Message message) {
			var msg = message as ClientMessage.DeleteStreamCompleted;
			if (msg != null) {
				switch (msg.Result) {
					case OperationResult.Success:
						return new ResponseConfiguration(HttpStatusCode.NoContent, "Stream deleted", "text/plain",
							Helper.UTF8NoBom);
					case OperationResult.PrepareTimeout:
					case OperationResult.CommitTimeout:
					case OperationResult.ForwardTimeout:
						return InternalServerError("Delete timeout");
					case OperationResult.WrongExpectedVersion:
						return new ResponseConfiguration(HttpStatusCode.BadRequest, "Wrong expected EventNumber",
							"text/plain", Helper.UTF8NoBom,
							new KeyValuePair<string, string>(SystemHeaders.CurrentVersion,
								msg.CurrentVersion.ToString()));
					case OperationResult.StreamDeleted:
						return Gone("Stream deleted");
					case OperationResult.InvalidTransaction:
						return InternalServerError("Invalid transaction");
					case OperationResult.AccessDenied:
						return Unauthorized();
					default:
						throw new ArgumentOutOfRangeException();
				}
			}

			var notHandled = message as ClientMessage.NotHandled;
			if (notHandled != null)
				return HandleNotHandled(entity.RequestedUrl, notHandled);
			return InternalServerError();
		}

		private static ResponseConfiguration HandleNotHandled(Uri requestedUri, ClientMessage.NotHandled notHandled) {
			switch (notHandled.Reason) {
				case ClientMessage.NotHandled.Types.NotHandledReason.NotReady:
					return ServiceUnavailable("Server Is Not Ready");
				case ClientMessage.NotHandled.Types.NotHandledReason.TooBusy:
					return ServiceUnavailable("Server Is Too Busy");
				case ClientMessage.NotHandled.Types.NotHandledReason.NotLeader: {
					var leaderInfo = notHandled.LeaderInfo;
					if (leaderInfo == null)
						return InternalServerError("No leader info available in response");
					return TemporaryRedirect(requestedUri, leaderInfo.Http.GetHost(), leaderInfo.Http.GetPort());
				}
				case ClientMessage.NotHandled.Types.NotHandledReason.IsReadOnly: {
					var leaderInfo = notHandled.LeaderInfo;
					if (leaderInfo == null)
						return InternalServerError("No leader info available in response");
					return DenyRequestBecauseReadOnly(requestedUri, leaderInfo.Http.GetHost(), leaderInfo.Http.GetPort());
				}
				default:
					return InternalServerError(string.Format("Unknown not handled reason: {0}", notHandled.Reason));
			}
		}

		public static ResponseConfiguration
			GetFreshStatsCompleted(HttpResponseConfiguratorArgs entity, Message message) {
			var completed = message as MonitoringMessage.GetFreshStatsCompleted;
			if (completed == null)
				return InternalServerError();

			var cacheSeconds = (int)MonitoringService.MemoizePeriod.TotalSeconds;
			return completed.Success
				? Ok(entity.ResponseCodec.ContentType, Helper.UTF8NoBom, null, cacheSeconds, isCachePublic: true)
				: NotFound();
		}

		public static ResponseConfiguration GetReplicationStatsCompleted(HttpResponseConfiguratorArgs entity,
			Message message) {
			var completed = message as ReplicationMessage.GetReplicationStatsCompleted;
			if (completed == null)
				return InternalServerError();

			var cacheSeconds = (int)MonitoringService.MemoizePeriod.TotalSeconds;
			return Ok(entity.ResponseCodec.ContentType, Helper.UTF8NoBom, null, cacheSeconds, isCachePublic: true);
		}

		public static ResponseConfiguration GetFreshTcpConnectionStatsCompleted(HttpResponseConfiguratorArgs entity,
			Message message) {
			var completed = message as MonitoringMessage.GetFreshTcpConnectionStatsCompleted;
			if (completed == null)
				return InternalServerError();

			var cacheSeconds = (int)MonitoringService.MemoizePeriod.TotalSeconds;
			return Ok(entity.ResponseCodec.ContentType, Helper.UTF8NoBom, null, cacheSeconds, isCachePublic: true);
		}
	}
}
