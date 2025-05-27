// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using EventStore.Client;
using EventStore.Client.Streams;
using EventStore.ClientAPI;
using EventStore.ClientAPI.SystemData;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using KurrentDB.Core.Services.Transport.Grpc;
using NUnit.Framework;
using ContentType = KurrentDB.Transport.Http.ContentType;
using StatusCode = Grpc.Core.StatusCode;

namespace KurrentDB.Core.Tests.Integration;

public abstract class authenticated_requests_made_from_a_follower<TLogFormat, TStreamId> : specification_with_cluster<TLogFormat, TStreamId> {
	protected const string ProtectedStream = "$foo";

	protected static readonly string AuthorizationHeaderValue =
		$"Basic {Convert.ToBase64String(Encoding.ASCII.GetBytes("admin:changeit"))}";

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class via_grpc_should : authenticated_requests_made_from_a_follower<TLogFormat, TStreamId> {
		private Status _status;

		protected override async Task Given() {
			var node = GetFollowers()[0];
			await Task.WhenAll(node.AdminUserCreated, node.Started);

			using var channel = GrpcChannel.ForAddress(new Uri($"https://{node.HttpEndPoint}"),
				new GrpcChannelOptions {
					HttpClient = new HttpClient(new SocketsHttpHandler {
						SslOptions = {
							RemoteCertificateValidationCallback = delegate { return true; }
						}
					}, true)
				});
			var streamClient = new Streams.StreamsClient(channel);
			using var call = streamClient.Append(new CallOptions(
				credentials: CallCredentials.FromInterceptor((_, metadata) => {
					metadata.Add("authorization", AuthorizationHeaderValue);
					return Task.CompletedTask;
				}),
				deadline: DateTime.UtcNow.AddSeconds(10)));

			await call.RequestStream.WriteAsync(new AppendReq {
				Options = new AppendReq.Types.Options {
					NoStream = new Empty(),
					StreamIdentifier = new StreamIdentifier {
						StreamName = ByteString.CopyFromUtf8(ProtectedStream)
					}
				}
			});
			await call.RequestStream.WriteAsync(new AppendReq {
				ProposedMessage = new AppendReq.Types.ProposedMessage {
					Id = new UUID {
						String = Uuid.FromGuid(Guid.NewGuid()).ToString()
					},
					CustomMetadata = ByteString.Empty,
					Data = ByteString.Empty,
					Metadata = {
						{Core.Services.Transport.Grpc.Constants.Metadata.Type, "-"}, {
							Core.Services.Transport.Grpc.Constants.Metadata.ContentType,
							Core.Services.Transport.Grpc.Constants.Metadata.ContentTypes
								.ApplicationOctetStream
						}
					}
				}
			});
			await call.RequestStream.CompleteAsync();
			await call.ResponseHeadersAsync;
			await call.ResponseAsync;
			_status = call.GetStatus();

			await base.Given();
		}

		[Test]
		[Retry(5)]
		public void work() => Assert.AreEqual(StatusCode.OK, _status.StatusCode);
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class via_tcp_should : authenticated_requests_made_from_a_follower<TLogFormat, TStreamId> {
		private Exception _caughtException;

		protected override async Task Given() {
			var node = GetFollowers()[0];
			await Task.WhenAll(node.AdminUserCreated, node.Started);

			using var connection = EventStoreConnection.Create(ConnectionSettings.Create()
					.DisableServerCertificateValidation()
					.PreferFollowerNode(),
				node.ExternalTcpEndPoint);
			await connection.ConnectAsync();

			try {
				await connection.AppendToStreamAsync(ProtectedStream, ExpectedVersion.NoStream,
					new UserCredentials("admin", "changeit"),
					new EventData(Guid.NewGuid(), "-", false, Array.Empty<byte>(), Array.Empty<byte>()));
			} catch (Exception ex) {
				_caughtException = ex;
			}

			await base.Given();
		}

		[Test]
		[Retry(5)]
		public void work() => Assert.Null(_caughtException);
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class via_http_should<TLogFormat, TStreamId> : authenticated_requests_made_from_a_follower<TLogFormat, TStreamId> {
	private HttpStatusCode _statusCode;
	private byte[] _error;

	protected override async Task Given() {
		var node = GetFollowers()[0];
		await Task.WhenAll(node.AdminUserCreated, node.Started);
		using var httpClient = new HttpClient(new SocketsHttpHandler {
			SslOptions = {
				RemoteCertificateValidationCallback = delegate { return true; }
			}
		}, true) {
			BaseAddress = new Uri($"https://{node.HttpEndPoint}/"),
			DefaultRequestHeaders = {
				Authorization = AuthenticationHeaderValue.Parse(AuthorizationHeaderValue)
			}
		};

		var content = JsonSerializer.SerializeToUtf8Bytes(new[] {
			new {
				eventId = Guid.NewGuid(),
				data = new{},
				metadata = new{},
				eventType = "-"
			}
		}, new JsonSerializerOptions {
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		});

		using var response = await httpClient.PostAsync($"/streams/{ProtectedStream}",
			new ReadOnlyMemoryContent(content) {
				Headers = { ContentType = new MediaTypeHeaderValue(ContentType.EventsJson) }
			});

		_statusCode = response.StatusCode;

		if (_statusCode is HttpStatusCode.InternalServerError) {
			_error = await response.Content.ReadAsByteArrayAsync();
		}
	}

	[Test]
	public void work() {
		if (_error is { Length: > 0 }) {
			Assert.Fail(Encoding.UTF8.GetString(_error));
		} else {
			Assert.AreEqual(HttpStatusCode.Created, _statusCode);
		}
	}
}
