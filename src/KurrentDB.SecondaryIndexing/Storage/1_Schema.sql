create table if not exists event_type (
	id bigint primary key not null,
	name varchar not null,
	unique(name)
);

create table if not exists category (
	id bigint primary key not null,
	name varchar not null,
	unique(name)
);

create table if not exists streams (
	id bigint primary key not null,
	name varchar not null,
	unique(name),
	max_age int DEFAULT NULL,
	max_count int DEFAULT NULL
);

create table if not exists idx_all (
	seq bigint not null,
	event_number bigint not null,
	log_position bigint not null,
	created timestamp not null,
	stream bigint not null,
	event_type bigint not null,
	event_type_seq bigint not null,
	category bigint not null,
	category_seq bigint not null,
);

create index if not exists idx_all_category on idx_all(category, category_seq);
create index if not exists idx_all_event_type on idx_all(event_type, category_seq);
create index if not exists idx_sequence on idx_all(seq);

create or replace macro read_category(name, startAt, finishAt) as table
select
    category_seq as seq,
	event->>'stream_id' as stream_id,
	event_number,
	event->>'event_type' as event_type,
	created,
	event->>'data' as data,
	event->>'metadata' as metadata,
from (
	select category_seq, event_number, created, kdb_get(log_position)::JSON as event from (
		select idx_all.category_seq, idx_all.log_position, idx_all.event_number, idx_all.created
		from idx_all
		inner join category on idx_all.category=category.id
		where category.name=name and idx_all.category_seq>=startAt and idx_all.category_seq<=finishAt
	)
) order by category_seq;

create or replace macro read_all(position) as table
select
	seq,
	event->>'stream_id' as stream_id,
	event_number,
	event->>'event_type' as event_type,
	created,
	event->>'data' as data,
	event->>'metadata' as metadata
from (
	select k.*, kdb_get(k.log_position)::JSON as event
	from (select seq, event_number, log_position, created from idx_all where seq > position) k
);

create or replace macro read_category(name, start, count) as table
select
	category_seq as seq,
	event->>'stream_id' as stream_id,
	event_number,
	event->>'event_type' as event_type,
	created,
	event->>'data' as data,
	event->>'metadata' as metadata
from (
	select idx_all.category_seq, idx_all.event_number, idx_all.created, kdb_get(log_position)::JSON as event
	from idx_all
	inner join category on idx_all.category=category.id
	where category.name=name and category_seq>=start and category_seq<start+count
);
