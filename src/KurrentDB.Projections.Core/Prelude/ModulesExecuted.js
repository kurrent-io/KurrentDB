"use strict";


var modules = (function() {
	var loadedModules = {};
	var modules = {
		require: function(moduleName) {
			var module = loadedModules[moduleName];

			if (module === undefined) {
				module = modules.$load_module(moduleName);
				loadedModules[moduleName] = module;
			}

			return module;
		}
	};
	return modules;
})();

modules;
