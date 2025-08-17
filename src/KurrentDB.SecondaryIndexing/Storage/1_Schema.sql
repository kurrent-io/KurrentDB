create table if not exists event_types (
	id int4 not null,
	name varchar not null,
	unique(name)
);

create table if not exists categories (
	id int4 not null,
	name varchar not null,
	unique(name)
);

create table if not exists streams (
	id bigint not null,
	name varchar not null,
	name_hash ubigint not null,
	max_age bigint DEFAULT NULL,
	max_count bigint DEFAULT NULL,
	is_deleted boolean DEFAULT false,
	truncate_before bigint DEFAULT NULL,
	acl varchar DEFAULT NULL
);

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
		inner join categories on idx_all.category_id=categories.id
		where categories.name=name and log_position>=start
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
		select idx_all.log_position, idx_all.event_number, idx_all.created, kdb_get(log_position)::JSON as event
		from idx_all
		inner join categories on idx_all.category_id=categories.id
		where categories.name=name
		order by log_position
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
		inner join categories on idx_all.category_id=categories.id
		where categories.name=name and log_position>=start
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
		where stream_id=name
	) order by event_number
;
