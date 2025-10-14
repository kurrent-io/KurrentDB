fromAll()
	.when({
		$init: function () {return {count:0}},
		tested: function (s, e) {s.count++},
	})
	.transformBy(function (state) {return {Total: state.count}})
