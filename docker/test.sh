#!/usr/bin/env sh
set -e

tests_directory=/build/published-tests
settings=/build/ci/ci.runsettings
output_directory=/build/test-results

tests=$(find "$tests_directory" -maxdepth 1 -type d -name "*.Tests")

for test in $tests; do
    proj=$(basename "$test")

    dotnet test \
      --blame \
      --blame-hang-timeout 5min \
      --settings "$settings" \
      --logger:"GitHubActions;report-warnings=false" \
      --logger:html \
      --logger:trx \
      --logger:"console;verbosity=normal" \
      --results-directory "$output_directory/$proj" "$test/$proj.dll"

    html_files=$(find "$output_directory/$proj" -name "*.html")

    for html in $html_files; do
      cat "$html" > "$output_directory/test-results.html"
    done
done
