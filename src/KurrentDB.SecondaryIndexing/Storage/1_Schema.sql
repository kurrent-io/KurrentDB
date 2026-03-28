create table idx_all (
	log_position bigint not null,
	commit_position bigint null,
	stream_revision bigint not null,
	created_at bigint not null,
	expires_at bigint null,
	stream varchar not null,
	stream_hash ubigint not null,
	schema_name varchar not null,
	category varchar not null,
	deleted boolean not null,
	schema_id varchar null,
	schema_format varchar not null,
	record_id blob not null
);

create table idx_user_checkpoints (
	index_name varchar primary key,
	log_position bigint not null,
	commit_position bigint null,
	created bigint not null
);

create table idx_metadata(
	key varchar primary key not null,
	value varchar
);
