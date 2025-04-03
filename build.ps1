[CmdletBinding()]
Param(
    [Parameter(HelpMessage="Assembly version number (x.y.z.0 where x.y.z is the semantic version)")]
    [string]$Version = "0.0.0.0",
    [Parameter(HelpMessage="Configuration (Debug, Release)")]
    [ValidateSet("Debug","Release")]
    [string]$Configuration = "Release",
    [Parameter(HelpMessage="Run Tests (yes,no)")]
    [ValidateSet("yes","no")]
    [string]$RunTests = "no"
)

Function Write-Info {
    Param([string]$message)
    Process {
        Write-Host $message -ForegroundColor Cyan
    }
}

#Borrowed from psake
Function Exec
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true, Position=0)][scriptblock]$Command,
        [Parameter(Mandatory=$false, Position=1)][string]$ErrorMessage = ("Failed executing {0}" -F $Command)
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw ("Exec: " + $ErrorMessage)
    }
}

Function Start-Build{
    #Make compatible with Powershell 2
    if(!$PSScriptRoot) { $PSScriptRoot = Split-Path $MyInvocation.MyCommand.Path -Parent }

    #Configuration
    $platform = "x64"

    $baseDirectory = $PSScriptRoot
    $srcDirectory = Join-Path $baseDirectory "src"
    $binDirectory = Join-Path $baseDirectory "bin"
    $eventStoreSolution = Join-Path $srcDirectory "EventStore.sln"

    Write-Info "Build Configuration"
    Write-Info "-------------------"

    Write-Info "Version: $Version"
    Write-Info "Platform: $platform"
    Write-Info "Configuration: $Configuration"
    Write-Info "Run Tests: $RunTests"

    #Build Event Store (Patch AssemblyInfo, Build, Revert AssemblyInfo)
    Remove-Item -Force -Recurse $binDirectory -ErrorAction SilentlyContinue > $null

    $versionInfoFile = Resolve-Path (Join-Path $srcDirectory (Join-Path "EventStore.Common" (Join-Path "Utils" "VersionInfo.cs"))) -Relative
    try {
        Exec { dotnet build -c $configuration /p:Version=$Version /p:Platform=x64 $eventStoreSolution }
    } finally {
        Write-Info "Reverting $versionInfoFile to original state."
        & { git checkout --quiet $versionInfoFile }
    }
    if($RunTests -eq "yes"){
        (Get-ChildItem -Attributes Directory src | % FullName) -Match '.Tests' | `
        ForEach-Object {
          dotnet test -v normal -c $Configuration /p:Platform=x64 --no-build --logger trx --results-directory testResults $_
          if (-Not $?) { throw "Exit code is $?" }
        }
    }
}

Start-Build
