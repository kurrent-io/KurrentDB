create table if not exists idx_all (
	log_position bigint not null,
	commit_position bigint null,
	event_number bigint not null,
	created bigint not null,
	expires bigint null,
	stream varchar not null,
	name_hash ubigint not null,
	event_type varchar not null,
	category varchar not null,
	is_deleted boolean not null,
	schema_id varchar null,
	schema_format varchar not null
);

-- Returns the latest (highest event_number) event per stream for streams matching the given mask
-- The mask is used with SQL LIKE semantics, e.g. 'orders-%'
create or replace macro read_stream_heads(mask) as table
	select
		log_position,
		event->>'stream_id' as stream_id,
		event_number,
		event->>'event_type' as event_type,
		epoch_ms(created) as created_at,
		event->>'data' as data,
		event->>'metadata' as metadata
	from (
		select k.log_position, k.event_number, k.created, kdb_get(k.log_position)::JSON as event
		from (
			select log_position, event_number, created
			from (
				select log_position, event_number, created, stream,
					row_number() over (partition by stream order by event_number desc) as rn
				from idx_all
				where stream like mask
			) h
			where rn = 1
			order by log_position
		) k
	)
;
