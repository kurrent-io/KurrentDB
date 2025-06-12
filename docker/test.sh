#!/usr/bin/env sh
set -e

tests_directory=/build/published-tests
settings=/build/ci/ci.runsettings
output_directory=/build/test-results

tests=$(find "$tests_directory" -maxdepth 2 -type d -name "*.Tests")

for test in $tests; do
    proj=$(basename "$test")

    if [ "$proj" = "KurrentDB.SchemaRegistry.Tests" ]; then
        dotnet exec \
          "$test/$proj.dll" \
          --report-trx \
          --report-junit \
          --results-directory "$output_directory/$proj"
    else
        dotnet test \
          --blame \
          --blame-hang-timeout 5min \
          --settings "$settings" \
          --logger:"GitHubActions;report-warnings=false" \
          --logger:trx \
          --logger:junit \
          --logger:"console;verbosity=normal" \
          --results-directory "$output_directory/$proj" "$test/$proj.dll"
    fi
done