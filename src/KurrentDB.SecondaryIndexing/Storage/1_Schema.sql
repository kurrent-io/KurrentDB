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
	stream_id bigint not null,
	event_type_id int4 not null,
	category_id int4 not null,
	is_deleted boolean not null
);
