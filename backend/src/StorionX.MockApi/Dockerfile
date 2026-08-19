FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

COPY backend/Directory.Build.props backend/global.json ./
COPY backend/src/EvMigration.Core/EvMigration.Core.csproj src/EvMigration.Core/
COPY backend/src/StorionX.MockApi/StorionX.MockApi.csproj src/StorionX.MockApi/
RUN dotnet restore src/StorionX.MockApi/StorionX.MockApi.csproj

COPY backend/src/EvMigration.Core/ src/EvMigration.Core/
COPY backend/src/StorionX.MockApi/ src/StorionX.MockApi/
RUN dotnet publish src/StorionX.MockApi/StorionX.MockApi.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "StorionX.MockApi.dll"]
