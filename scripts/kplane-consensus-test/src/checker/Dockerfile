FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY checker.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "checker.dll"]
