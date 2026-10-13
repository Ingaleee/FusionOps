FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore FusionOps.Gateway/FusionOps.Gateway.csproj --configfile NuGet.Config --locked-mode
RUN dotnet publish FusionOps.Gateway/FusionOps.Gateway.csproj -c Release --no-restore -o /app/out /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/out .
ENTRYPOINT ["dotnet", "FusionOps.Gateway.dll"]
