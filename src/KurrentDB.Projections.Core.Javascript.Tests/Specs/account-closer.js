fromCategory("account").when(
	{
		"account-deleted": function(_, e) {
			emit('$'+`${e.streamId}`, '$metadata', { $tb: 9223372036854775807});
		}
	}
)
