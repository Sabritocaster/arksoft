FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

COPY backend/Directory.Build.props backend/global.json ./
COPY backend/src/EvMigration.Core/EvMigration.Core.csproj src/EvMigration.Core/
COPY backend/src/EvMigration.DemoApi/EvMigration.DemoApi.csproj src/EvMigration.DemoApi/
RUN dotnet restore src/EvMigration.DemoApi/EvMigration.DemoApi.csproj

COPY backend/src/EvMigration.Core/ src/EvMigration.Core/
COPY backend/src/EvMigration.DemoApi/ src/EvMigration.DemoApi/
RUN dotnet publish src/EvMigration.DemoApi/EvMigration.DemoApi.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
COPY backend/samples/target-archives.json /app/samples/target-archives.json
ENTRYPOINT ["dotnet", "EvMigration.DemoApi.dll"]
