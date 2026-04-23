options({
	biState: true
});

fromCategory("transaction")
	.partitionBy(function(e) {
		if (e.eventType === "header") return "description";
		if (e.body.accountId.startsWith("ESDBB"))
			return e.body.accountId;
		return undefined;
	})
	.when({
		$init: function() {
			return { balance: 0 };
		},
		$initShared: function() {
			return {
				numberOfAccounts: 0,
				totalBalance: 0
			};
		},
		$created: function(s, e) {
			if (e.partition !== "description") {
				s[1].numberOfAccounts++;
			} else {
				s[0] = null;
			}
		},
		"header": function(s, e) {
			s[1].description = e.body.description;
		},
		"credit": function(s, e) {
			if (e.partition === "") return {};
			s[0].balance += e.body.amount;
			s[0].credit = e.body.amount;
			s[0].debit = undefined;
			s[0].description = s[1].description;
			s[1].totalBalance += e.body.amount;
			return s;
		},
		"debit": function(s, e) {
			if (e.partition === "") return {};
			s[0].balance -= e.body.amount;
			s[0].credit = undefined;
			s[0].debit = e.body.amount;
			s[0].description = s[1].description;
			s[1].totalBalance -= e.body.amount;
			return s;
		}
	})
	.filterBy(function(s) {
		return s.partition !== "description";
	}).transformBy(function(s) {
		s.partition = undefined;
	})
	.outputTo("total-balance", "account-balance-{0}");
