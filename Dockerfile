FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props DsarClock.slnx ./
COPY src ./src
RUN dotnet publish src/DsarClock.Api -c Release -o /out

# Chiseled: no shell, no package manager, non-root by default.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "DsarClock.Api.dll"]
