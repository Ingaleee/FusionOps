# build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY FusionOps.sln .
COPY FusionOps.Application/FusionOps.Application.csproj FusionOps.Application/
COPY FusionOps.Domain/FusionOps.Domain.csproj FusionOps.Domain/
COPY FusionOps.Infrastructure/FusionOps.Infrastructure.csproj FusionOps.Infrastructure/
COPY FusionOps.Presentation/FusionOps.Presentation.csproj FusionOps.Presentation/
RUN dotnet restore FusionOps.Presentation/FusionOps.Presentation.csproj
COPY . .
RUN dotnet publish FusionOps.Presentation/FusionOps.Presentation.csproj -c Release -o /app/publish /p:UseAppHost=false

# runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:80
EXPOSE 80
ENTRYPOINT ["dotnet", "FusionOps.Presentation.dll"]
