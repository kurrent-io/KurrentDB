# "build" image
ARG CONTAINER_RUNTIME=noble
# NOT A BUG: we can't build on alpine so we use noble as a base image
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
ARG RUNTIME=linux-x64

WORKDIR /build
COPY ./LICENSE.md .
COPY ./LICENSE_CONTRIBUTIONS.md .
COPY ./NOTICE.md .

WORKDIR /build/ci
COPY ./ci ./

WORKDIR /build/proto
COPY ./proto ./

WORKDIR /build/src
COPY ./src/Connectors/*/*.csproj ./Connectors/
COPY ./src/SchemaRegistry/*/*.csproj ./SchemaRegistry/
COPY ./src/KurrentDB.sln ./src/*/*.csproj ./src/Directory.Build.* ./src/Directory.Packages.props ./
RUN for file in $(ls Connectors/*.csproj); do mkdir -p ./${file%.*}/ && mv $file ./${file%.*}/; done && \
    for file in $(ls SchemaRegistry/*.csproj); do mkdir -p ./${file%.*}/ && mv $file ./${file%.*}/; done && \
    for file in $(ls *.csproj); do mkdir -p ./${file%.*}/ && mv $file ./${file%.*}/; done && \
    dotnet restore --runtime=${RUNTIME}
COPY ./src .

WORKDIR /build/.git
COPY ./.git/ .

# "test" image
FROM mcr.microsoft.com/dotnet/sdk:10.0-${CONTAINER_RUNTIME} AS test
WORKDIR /build
COPY --from=build ./build/src ./src
COPY --from=build ./build/ci ./ci
COPY --from=build ./build/proto ./proto
COPY --from=build ./build/LICENSE.md ./LICENSE.md
COPY --from=build ./build/NOTICE.md ./NOTICE.md
COPY --from=build ./build/LICENSE_CONTRIBUTIONS.md ./LICENSE_CONTRIBUTIONS.md
COPY --from=build ./build/src/KurrentDB.Core.Tests/Services/Transport/Tcp/test_certificates/ca/ca.crt /usr/local/share/ca-certificates/ca_kurrentdb_test.crt
RUN mkdir ./test-results

SHELL ["/bin/bash", "-c"]
CMD dotnet test \
    --settings "/build/ci/ci.container.runsettings" \
    --blame \
    --blame-hang-timeout 5min \
    --logger:trx \
    --logger:"GitHubActions;report-warnings=false" \
    --logger:"console;verbosity=normal" \
    --results-directory "/build/test-results" \
    /build/src/KurrentDB.sln \
    -- --report-trx --results-directory "/build/test-results"

# "publish" image
FROM build AS publish
ARG RUNTIME=linux-x64

RUN dotnet publish --configuration=Release --runtime=${RUNTIME} --self-contained \
     --framework=net10.0 --output /publish /build/src/KurrentDB

# "runtime" image
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-${CONTAINER_RUNTIME} AS runtime
ARG RUNTIME=linux-x64
ARG UID=1001
ARG GID=1001

RUN if [[ "${RUNTIME}" = "linux-musl-x64" ]];\
    then \
        apk update && \
        apk add --no-cache \
        curl; \
    else \
        apt update && \
        apt install -y \
        adduser \
        curl && \
        rm -rf /var/lib/apt/lists/*; \
    fi

WORKDIR /opt/kurrentdb

RUN addgroup --gid ${GID} "kurrent" && \
    adduser \
    --disabled-password \
    --gecos "" \
    --ingroup "kurrent" \
    --no-create-home \
    --uid ${UID} \
    "kurrent"

COPY --chown=kurrent:kurrent --from=publish /publish ./

RUN mkdir -p /var/lib/kurrentdb && \
    mkdir -p /var/log/kurrentdb && \
    mkdir -p /etc/kurrentdb && \
    chown -R kurrent:kurrent /var/lib/kurrentdb /var/log/kurrentdb /etc/kurrentdb

USER kurrent

RUN printf "NodeIp: 0.0.0.0\n\
ReplicationIp: 0.0.0.0" >> /etc/kurrentdb/kurrentdb.conf

VOLUME /var/lib/kurrentdb /var/log/kurrentdb

EXPOSE 1112/tcp 1113/tcp 2113/tcp

HEALTHCHECK --interval=5s --timeout=5s --retries=24 \
    CMD curl --fail --insecure https://localhost:2113/health/live || curl --fail http://localhost:2113/health/live || exit 1

ENTRYPOINT ["/opt/kurrentdb/KurrentDB"]
