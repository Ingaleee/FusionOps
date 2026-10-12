# build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY FusionOps.sln .
COPY global.json NuGet.Config Directory.Build.props ./
COPY FusionOps.Application/FusionOps.Application.csproj FusionOps.Application/packages.lock.json FusionOps.Application/
COPY FusionOps.Domain/FusionOps.Domain.csproj FusionOps.Domain/packages.lock.json FusionOps.Domain/
COPY FusionOps.Infrastructure/FusionOps.Infrastructure.csproj FusionOps.Infrastructure/packages.lock.json FusionOps.Infrastructure/
COPY FusionOps.Presentation/FusionOps.Presentation.csproj FusionOps.Presentation/packages.lock.json FusionOps.Presentation/
COPY FusionOps.Infrastructure.SagaContracts/FusionOps.Infrastructure.SagaContracts.csproj FusionOps.Infrastructure.SagaContracts/packages.lock.json FusionOps.Infrastructure.SagaContracts/
RUN dotnet restore FusionOps.Presentation/FusionOps.Presentation.csproj --configfile NuGet.Config --locked-mode
COPY . .
RUN dotnet publish FusionOps.Presentation/FusionOps.Presentation.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

# runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:80
EXPOSE 80
ENTRYPOINT ["dotnet", "FusionOps.Presentation.dll"]
