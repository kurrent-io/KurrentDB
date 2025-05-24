# "build" image
ARG CONTAINER_RUNTIME=jammy
# NOT A BUG: we can't build on alpine so we use jammy as a base image
FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS build
ARG RUNTIME=linux-x64

WORKDIR /build
COPY ./LICENSE.md .
COPY ./LICENSE_CONTRIBUTIONS.md .
COPY ./NOTICE.html .
COPY ./docker ./scripts

WORKDIR /build/ci
COPY ./ci ./

WORKDIR /build/src
COPY ./src/Connectors/*/*.csproj ./Connectors/
RUN for file in $(ls Connectors/*.csproj); do mkdir -p ./${file%.*}/ && mv $file ./${file%.*}/; done
COPY ./src/KurrentDB.sln ./src/*/*.csproj ./src/Directory.Build.* ./src/Directory.Packages.props ./
RUN for file in $(ls *.csproj); do mkdir -p ./${file%.*}/ && mv $file ./${file%.*}/; done
RUN dotnet restore --runtime=${RUNTIME}
COPY ./src .

WORKDIR /build/.git
COPY ./.git/ .

RUN /build/scripts/build.sh /build/src /build/published-tests

# "test" image
FROM mcr.microsoft.com/dotnet/sdk:8.0-${CONTAINER_RUNTIME} as test
WORKDIR /build
COPY --from=build ./build/published-tests ./published-tests
COPY --from=build ./build/ci ./ci
COPY --from=build ./build/scripts ./scripts
COPY --from=build ./build/src/KurrentDB.Core.Tests/Services/Transport/Tcp/test_certificates/ca/ca.crt /usr/local/share/ca-certificates/ca_kurrentdb_test.crt
RUN mkdir ./test-results

CMD ["/build/scripts/test.sh"]

# "publish" image
FROM build as publish
ARG RUNTIME=linux-x64

RUN dotnet publish --configuration=Release --runtime=${RUNTIME} --self-contained \
     --framework=net8.0 --output /publish /build/src/KurrentDB

# "runtime" image
FROM mcr.microsoft.com/dotnet/runtime-deps:8.0-${CONTAINER_RUNTIME} AS runtime
ARG RUNTIME=linux-x64
ARG UID=1000
ARG GID=1000

RUN if [[ "${RUNTIME}" = "linux-musl-x64" ]];\
    then \
        apk update && \
        apk add --no-cache \
        curl; \
    else \
        apt update && \
        apt install -y \
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
