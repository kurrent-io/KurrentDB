fromAll()
	.when({
		$init: function () {return {d: []}},
		with_null: function (s, e) {s.d[e.sequenceNumber]=e.sequenceNumber},
	})
	.transformBy(function (state) {return null;});
