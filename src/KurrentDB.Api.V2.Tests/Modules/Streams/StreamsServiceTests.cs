
using KurrentDB.Api.Tests.Fixtures;
using KurrentDB.Protocol.V2.Streams;

using static KurrentDB.Protocol.V2.Streams.StreamsService;

namespace KurrentDB.Api.Tests.Streams;

public class StreamsServiceTests : ApiTestFixture {
	[Test]
	public async Task appends_records_to_single_stream() {
		// Arrange
		var client = new StreamsServiceClient(GrpcChannel);

		var names = new[] { "James", "Jo", "Lee" };

		// Act
		using var session = client.AppendSession();

		foreach (var name in names)
		{
			await session.RequestStream.WriteAsync(new AppendRequest {
				Stream           = "kebas",
				ExpectedRevision = 1,
				Records = {
					new AppendRecord {
						RecordId  = null,
						Timestamp = 0,
						Schema    = null,
						Data      = null,
						Properties = {  }
					}
				}
			});
		}

		await session.RequestStream.CompleteAsync();

		var response = await session;

		// // Assert
		// Assert.Equal("Hello James, Jo, Lee", response.Output);
	}

	[Test]
	public async Task appends_records_to_many_streams() {

	}

	[Test]
	public async Task throws_when_user_does_not_have_premissions() {

	}

	[Test]
	public async Task throws_when_stream_already_tracked() {

	}

	[Test]
	public async Task throws_when_record_is_too_large() {

	}

	[Test]
	public async Task throws_when_transaction_is_too_large() {

	}

	[Test]
	public async Task throws_on_stream_revision_conflict() {

	}

	[Test]
	public async Task throws_on_stream_tombstoned() {

	}

	[Test]
	public async Task throws_when_request_has_no_records() {

	}

	[Test]
	public async Task throws_when_stream_name_is_invalid() {

	}

	[Test]
	public async Task throws_when_record_id_is_invalid() {

	}

	[Test]
	public async Task throws_when_schema_name_is_invalid() {

	}

	[Test]
	public async Task throws_when_schema_format_is_invalid() {

	}

	[Test]
	public async Task throws_when_schema_id_is_invalid() {

	}
}
