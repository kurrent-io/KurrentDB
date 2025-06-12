#!/usr/bin/env sh

tests_directory=./src
settings=/build/ci/ci.runsettings
output_directory=/build/test-results

dotnet test \ 
  --configuration Release \
  --blame \
  --blame-hang-timeout 5min \
  --blame-hang-dump-type mini \
  --settings "$settings" \
  --logger:GitHubActions \
  --logger:html \
  --logger:trx \
  --logger:"console;verbosity=normal" \
  --results-directory "$output_directory" \
  "$tests_directory/KurrentDB.sln" \
  -- --report-trx --results-directory "$output_directory"