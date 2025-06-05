#!/usr/bin/env sh
set -e

tests_directory=/build/published-tests
settings=/build/ci/ci.runsettings
output_directory=/build/test-results

tests=$(find "$tests_directory" -maxdepth 2 -type d -name "*.Tests")

for test in $tests; do
    proj=$(basename "$test")

    if [ "$proj" = "KurrentDB.SchemaRegistry.Tests" ]; then
        dotnet exec "$test/$proj.dll" \
          --timeout 5m \
          --report-trx \
          --results-directory "$output_directory/$proj"
    else
        dotnet test \
          --blame \
          --blame-hang-timeout 5min \
          --settings "$settings" \
          --logger:"GitHubActions;report-warnings=false" \
          --logger:html \
          --logger:trx \
          --logger:"console;verbosity=normal" \
          --results-directory "$output_directory/$proj" "$test/$proj.dll"
    fi

    html_files=$(find "$output_directory/$proj" -name "*.html")
    trx_files=$(find "$output_directory/$proj" -name "*.trx")

    for trx in $trx_files; do
      cat "$trx" > "$output_directory/test-results.trx"
    done

    for html in $html_files; do
      cat "$html" > "$output_directory/test-results.html"
    done
done
