# MQEL private server — Gameserver (Step 1: capture host). Multi-stage build, .NET 8 LTS.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY MQELServer.sln ./
COPY src/ ./src/
RUN dotnet restore
RUN dotnet publish src/MQEL.Gameserver/MQEL.Gameserver.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
# Captured client traffic is written here — mount a volume so it survives container restarts.
VOLUME /app/captures
EXPOSE 8080
ENTRYPOINT ["dotnet", "MQEL.Gameserver.dll"]
