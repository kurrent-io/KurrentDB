create table if not exists idx_all (
	log_position bigint not null,
	commit_position bigint null,
	event_number bigint not null,
	created bigint not null,
	expires bigint null,
	stream varchar not null,
	stream_hash ubigint not null,
	event_type varchar not null,
	category varchar not null,
	is_deleted boolean not null,
	schema_id varchar null,
	schema_format varchar not null
);

create table if not exists idx_custom_checkpoints (
	index_name varchar primary key,
	log_position bigint not null,
	commit_position bigint null,
	created bigint not null
);
