FROM mcr.microsoft.com/dotnet/sdk:8.0 AS dotnet
# This image supplies Debian 11/glibc 2.31 and native dependencies only.
# The SDK and managed runtime below are .NET 8, not .NET 6.
FROM mcr.microsoft.com/dotnet/runtime-deps:6.0-bullseye-slim
# Freeze this legacy ABI environment before Bullseye's security archive moves.
# Snapshot metadata remains signed; its historical expiry is expected.
RUN printf '%s\n' \
      'deb [check-valid-until=no] https://snapshot.debian.org/archive/debian/20260801T000000Z bullseye main' \
      'deb [check-valid-until=no] https://snapshot.debian.org/archive/debian-security/20260801T000000Z bullseye-security main' \
      'deb [check-valid-until=no] https://snapshot.debian.org/archive/debian/20260801T000000Z bullseye-updates main' \
      > /etc/apt/sources.list \
    && apt-get update && apt-get install -y --no-install-recommends python3 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=dotnet /usr/share/dotnet /usr/share/dotnet
ENV DOTNET_ROOT=/usr/share/dotnet PATH=/usr/share/dotnet:$PATH DOTNET_CLI_HOME=/tmp/dotnet-cli DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
