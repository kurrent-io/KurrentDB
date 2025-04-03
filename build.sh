#!/usr/bin/env bash

BASE_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PRODUCTNAME="Event Store Open Source"
COMPANYNAME="Event Store Ltd"
COPYRIGHT="Copyright 2021 Event Store Ltd. All rights reserved."


# ------------ End of configuration -------------

CONFIGURATION="Release"
NET_FRAMEWORK="net8.0"

function usage() {    
cat <<EOF
Usage:
  $0 [<version=0.0.0.0>] [<configuration=Debug|Release>]

version: EventStore build version. Versions must be complete four part identifiers valid for use on a .NET assembly.

configuration: Build configuration. Valid configurations are: Debug, Release

EOF
    exit 1
}

function detectOS(){
    unameOut="$(uname -s)"
    case "${unameOut}" in
        Linux*)     os=Linux;;
        Darwin*)    os=MacOS;;
        *)          os="${unameOut}"
    esac

    if [[ "$os" != "Linux" && $os != "MacOS" ]] ; then
        echo "Unsupported operating system: $os. Only Linux and MacOS are supported."
        exit 1
    fi
    OS=$os
}

function checkParams() {
    if [[ "$1" == "-help" || "$1" == "--help" ]] ; then
        usage
    fi

    version=$1
    configuration=$2

    [[ $# -gt 2 ]] && usage

    if [[ "$version" == "" ]] ; then
        VERSIONSTRING="0.0.0.0"
        echo "Version defaulted to: 0.0.0.0"
    else
        VERSIONSTRING=$version
        echo "Version set to: $VERSIONSTRING"
    fi

    if [[ "$configuration" == "" ]]; then
        CONFIGURATION="Release"
        echo "Configuration defaulted to: $CONFIGURATION"
    else
        if [[ "$configuration" == "Release" || "$configuration" == "Debug" ]]; then
            CONFIGURATION=$configuration
            echo "Configuration set to: $CONFIGURATION"
        else
            echo "Invalid configuration: $configuration"
            usage
        fi
    fi
}

function revertVersionInfo() {
    files=$( find . -name "VersionInfo.cs" )

    for file in $files
    do
        git checkout "$file"
        echo "Reverted $file"
    done
}

function err() {
    revertVersionInfo
    echo "FAILED. See earlier messages"
    exit 1
}

function patchVersionInfo {
    branchName=$(git rev-parse --abbrev-ref HEAD)
    commitHash=$(git log -n1 --pretty=format:"%H" HEAD)
    commitTimestamp=$(git log -n1 --pretty=format:"%aD" HEAD)

    newVersion="public static readonly string Version = \"$VERSIONSTRING\";"
    newBranch="public static readonly string Branch = \"$branchName\";"
    newCommitHash="public static readonly string Hashtag = \"$commitHash\";"
    newTimestamp="public static readonly string Timestamp = \"$commitTimestamp\";"

    versionPattern="public static readonly string Version .*$"
    branchPattern="public static readonly string Branch .*$"
    commitHashPattern="public static readonly string Hashtag .*$"
    timestampPattern="public static readonly string Timestamp .*$"

    files=$( find . -name "VersionInfo.cs" )

    for file in $files
    do
        tempfile="$file.tmp"
        sed -e "s/$versionPattern/$newVersion/" \
            -e "s/$branchPattern/$newBranch/" \
            -e "s/$commitHashPattern/$newCommitHash/" \
            -e "s/$timestampPattern/$newTimestamp/" \
            "$file" > "$tempfile"

        mv "$tempfile" "$file"
        echo "Patched $file with version information"
    done
}

function buildEventStore {
    patchVersionInfo
    rm -rf bin/
    dotnet build -c $CONFIGURATION /p:Platform=x64 /p:Version=$VERSIONSTRING --framework=$NET_FRAMEWORK src/EventStore.sln || err
    revertVersionInfo
}

function exitWithError {
    echo "$1"
    exit 1
}

detectOS
checkParams "$1" "$2"

echo "Running from base directory: $BASE_DIR"
buildEventStore
