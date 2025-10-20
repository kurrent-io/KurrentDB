# https://github.com/sensslen/nuget-license
# overridden-packages.json contains packages that couldn't be automatically determined

$commonArgs = @(
  "--allowed-license-types allowed-license-types.json"
  "--exclude-projects-matching ignored-projects.json"
  "--include-transitive"
  "--ignored-packages ignored-packages.json"
  "--input ../../KurrentDB.slnx"
  "--licenseurl-to-license-mappings license-url-mappings.json"
  "--override-package-information overridden-packages.json"
)

# set the working directory to be the script location
pushd -Path $PSScriptRoot
try { 
  dotnet build ../../src
  dotnet tool install --global nuget-license
  nuget-license @commonArgs --output Markdown --file-output ../../NOTICE.md
  echo Errors:
  nuget-license @commonArgs --output JsonPretty --error-only
} finally {
  popd
}
