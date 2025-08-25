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
	is_deleted boolean not null
);

create or replace macro read_all(position) as table
select
	log_position,
	event->>'stream_id' as stream_id,
	event_number,
	event->>'event_type' as event_type,
	epoch_ms(created) as created_at,
	event->>'data' as data,
	event->>'metadata' as metadata
from (
	select k.*, kdb_get(k.log_position)::JSON as event
	from (select event_number, log_position, created from idx_all where log_position >= position) k
	order by log_position
);

create or replace macro read_all() as table
select
	log_position,
	event->>'stream_id' as stream_id,
	event_number,
	event->>'event_type' as event_type,
	epoch_ms(created) as created_at,
	event->>'data' as data,
	event->>'metadata' as metadata
from (
	select k.*, kdb_get(k.log_position)::JSON as event
	from (select event_number, log_position, created from idx_all) k
	order by log_position
);

create or replace macro read_category(name, start) as table
	select
		log_position,
		event->>'stream_id' as stream_id,
		event_number,
		event->>'event_type' as event_type,
		epoch_ms(created) as created_at,
		event->>'data' as data,
		event->>'metadata' as metadata
	from (
		select idx_all.log_position, idx_all.event_number, idx_all.created, kdb_get(log_position)::JSON as event
		from idx_all
		where category=name and log_position>=start
		order by log_position
	);

create or replace macro read_category(name) as table
	select
		log_position,
		event->>'stream_id' as stream_id,
		event_number,
		event->>'event_type' as event_type,
		epoch_ms(created) as created_at,
		event->>'data' as data,
		event->>'metadata' as metadata
	from (
		select log_position, event_number, created, kdb_get(log_position)::JSON as event
		from (
		select log_position, event_number, created from idx_all where category=name
		union
		select log_position, event_number, created from inflight() where category=name
		order by log_position
		)
	)
;

create or replace macro read_stream(name, start) as table
	select
		log_position,
		event->>'stream_id' as stream_id,
		event_number,
		event->>'event_type' as event_type,
		epoch_ms(created) as created_at,
		event->>'data' as data,
		event->>'metadata' as metadata
	from (
		select idx_all.log_position, idx_all.event_number, idx_all.created, kdb_get(log_position)::JSON as event
		from idx_all
		where stream=name and log_position>=start
		order by log_position
	);

create or replace macro read_stream(name) as table
	select
		log_position,
		event->>'stream_id' as stream_id,
		event_number,
		event->>'event_type' as event_type,
		epoch_ms(created) as created_at,
		event->>'data' as data,
		event->>'metadata' as metadata
	from (
		select idx_all.log_position, idx_all.event_number, idx_all.created, kdb_get(log_position)::JSON as event
		from idx_all
		where stream=name
	) order by event_number
;

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
