using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Dapper;
using EventStore.Core.Data;
using EventStore.Core.Metrics;
using EventStore.Core.Services.Storage.ReaderIndex;
using Eventuous.Subscriptions.Context;

namespace EventStore.Core.Duck.Default;

class EventTypeIndexReader<TStreamId>(EventTypeIndex eventTypeIndex, IReadIndex<TStreamId> index) : DuckIndexReader<TStreamId>(index) {
	protected override long GetId(string streamName) {
		if (!streamName.StartsWith("$etype-")) {
			return EventNumber.Invalid;
		}

		var eventType = streamName[(streamName.IndexOf('-') + 1)..];
		return eventTypeIndex.EventTypes.TryGetValue(eventType, out var id) ? id : ExpectedVersion.NoStream;
	}

	protected override long GetLastNumber(long id) => eventTypeIndex.GetLastEventNumber(id);

	protected override IEnumerable<IndexedPrepare> GetIndexRecords(long id, long fromEventNumber, long toEventNumber)
		=> eventTypeIndex.GetRecords(id, fromEventNumber, toEventNumber);

	public override ValueTask<long> GetLastIndexedPosition() => ValueTask.FromResult(eventTypeIndex.LastPosition);

	public override bool OwnStream(string streamId) => streamId.StartsWith("$etype-");
}

public class EventTypeIndex(DuckDb db) {
	public Dictionary<long, string> EventTypeIds = new();
	public Dictionary<string, long> EventTypes = new();
	readonly Dictionary<long, long> Sequences = new();

	public void Init() {
		var connection = db.GetOrOpenConnection();
		List<ReferenceRecord> ids;
		try {
			ids = connection.Query<ReferenceRecord>("select * from event_type").ToList();
		} finally {
			db.ReturnConnection(connection);
		}

		EventTypeIds = ids.ToDictionary(x => x.id, x => x.name);
		EventTypes = ids.ToDictionary(x => x.name, x => x.id);
		Seq = EventTypeIds.Count > 0 ? EventTypeIds.Keys.Max() : 0;

		foreach (var id in ids) {
			Sequences[id.id] = -1;
		}

		const string query = "select event_type, max(event_type_seq) from idx_all group by event_type";

		connection = db.GetOrOpenConnection();
		try {
			var sequences = connection.Query<(long Id, long Sequence)>(query);
			foreach (var sequence in sequences) {
				Sequences[sequence.Id] = sequence.Sequence;
			}
		} finally {
			db.ReturnConnection(connection);
		}
	}

	public long GetLastEventNumber(long id) => Sequences.TryGetValue(id, out var size) ? size : ExpectedVersion.NoStream;

	public IEnumerable<IndexedPrepare> GetRecords(long id, long fromEventNumber, long toEventNumber) {
		var range = QueryEventType(id, fromEventNumber, toEventNumber);
		var indexPrepares = range.Select(x => new IndexedPrepare(x.event_type_seq, x.event_number, x.log_position));
		return indexPrepares;
	}

	// [MethodImpl(MethodImplOptions.Synchronized)]
	List<EventTypeRecord> QueryEventType(long eventTypeId, long fromEventNumber, long toEventNumber) {
		const string query = """
		                     select event_type_seq, log_position, event_number
		                     from idx_all where event_type=$et and event_type_seq>=$start and event_type_seq<=$end
		                     """;

		var duration = TempIndexMetrics.MeasureIndex("duck_get_et_range");
		var connection = db.GetOrOpenConnection();

		try {
			return connection.Query<EventTypeRecord>(query, new { et = eventTypeId, start = fromEventNumber, end = toEventNumber }).ToList();
		} finally {
			duration.Dispose();
			db.ReturnConnection(connection);
		}
	}

	public SequenceRecord Handle(IMessageConsumeContext ctx) {
		LastPosition = (long)ctx.GlobalPosition;
		if (EventTypes.TryGetValue(ctx.MessageType, out var val)) {
			var next = Sequences[val] + 1;
			Sequences[val] = next;
			return new(val, next);
		}

		var id = ++Seq;

		var connection = db.GetOrOpenConnection();
		try {
			connection.Execute(Sql, new { id, name = ctx.MessageType });
		} finally {
			db.ReturnConnection(connection);
		}

		EventTypes[ctx.MessageType] = id;
		EventTypeIds[id] = ctx.MessageType;
		Sequences[id] = 0;
		return new(id, 0);
	}

	internal long LastPosition { get; private set; }

	static long Seq;
	static readonly string Sql = Default.Sql.AppendIndexSql.Replace("{table}", "event_type");
}
