options({
	$includeLinks: true
});
fromAll().when({
	$any: (s, e) => {
		const stream = e.streamId.split("-")[0];
		linkTo(stream, e);
	}
});
