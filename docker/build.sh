#!/usr/bin/env sh

set -e
set -x

source_directory=$1
output_directory=$2

tests=$(find "$source_directory" -maxdepth 1 -type d -name "*.Tests")

# Publish tests
for test in $tests; do
    echo Publishing tests for $test

    dotnet publish \
      --runtime="$RUNTIME" \
      --no-self-contained \
      --configuration Release \
      --output "$output_directory/$(basename "$test")" \
      "$test"
done

cp "$source_directory"/*.sln "$output_directory"
