# syntax=docker/dockerfile:1

# ---- build -----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from the project files alone, so this layer is cached until a package or reference changes.
COPY src/MemberPulse.Domain/MemberPulse.Domain.csproj src/MemberPulse.Domain/
COPY src/MemberPulse.Infrastructure/MemberPulse.Infrastructure.csproj src/MemberPulse.Infrastructure/
COPY src/MemberPulse.Api/MemberPulse.Api.csproj src/MemberPulse.Api/
RUN dotnet restore src/MemberPulse.Api/MemberPulse.Api.csproj

COPY src/ src/
RUN dotnet publish src/MemberPulse.Api/MemberPulse.Api.csproj \
      --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

# ---- run -------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Listen on 8080 (a non-root user can't bind port 80) and run as the image's built-in unprivileged user.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID

# Configuration comes from the environment, e.g. ConnectionStrings__MemberPulse, Jwt__Key, Kafka__BootstrapServers.
# See docker-compose.yml and the README for the full list.
ENTRYPOINT ["dotnet", "MemberPulse.Api.dll"]
