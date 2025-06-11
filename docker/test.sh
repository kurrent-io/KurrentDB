#!/usr/bin/env sh

tests_directory=./src
settings=/build/ci/ci.runsettings
output_directory=/build/test-results

# dotnet test \
#   --configuration Release \
#   --blame \
#   --blame-hang-timeout 5min \
#   --blame-hang-dump-type mini \
#   --settings "$settings" \
#   --logger:GitHubActions \
#   --logger:html \
#   --logger:trx \
#   --logger:"console;verbosity=normal" \
#   --results-directory="$output_directory" \
#   "$tests_directory/Connectors/KurrentDB.Connectors.Tests/KurrentDB.Connectors.Tests.csproj"

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
  --results-directory="$output_directory" \
  "$tests_directory/KurrentDB.sln"



#set -e

#tests_directory=./src
#settings=/build/ci/ci.runsettings
#output_directory=/build/test-results
#
#csproj_files=$(find "$tests_directory" -type f -name "*.Tests.csproj")
#
#for csproj in $csproj_files; do
#    proj=$(basename "$(dirname "$csproj")")
#
#    dotnet test \
#      --blame \
#      --blame-hang-timeout 5min \
#      --settings "$settings" \
#      --logger:"GitHubActions;report-warnings=false" \
#      --logger:html \
#      --logger:trx \
#      --logger:"console;verbosity=normal" \
#      --results-directory "$output_directory/$proj" "$csproj"
#
#    html_files=$(find "$output_directory/$proj" -name "*.html")
#
#    for html in $html_files; do
#      cat "$html" > "$output_directory/test-results.html"
#    done
#
#done
