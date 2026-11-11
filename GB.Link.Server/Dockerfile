# syntax=docker/dockerfile:1
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY GB.Emulator.Core/GB.Emulator.Core.csproj GB.Emulator.Core/
COPY GB.Emulator.Link/GB.Emulator.Link.csproj GB.Emulator.Link/
COPY GB.Emulator.Hosting/GB.Emulator.Hosting.csproj GB.Emulator.Hosting/
COPY GB.Emulator.AI/GB.Emulator.AI.csproj GB.Emulator.AI/
COPY GB.Link.Server/GB.Link.Server.csproj GB.Link.Server/
RUN dotnet restore GB.Link.Server/GB.Link.Server.csproj -a $TARGETARCH
COPY GB.Emulator.Core/ GB.Emulator.Core/
COPY GB.Emulator.Link/ GB.Emulator.Link/
COPY GB.Emulator.Hosting/ GB.Emulator.Hosting/
COPY GB.Emulator.AI/ GB.Emulator.AI/
COPY GB.Link.Server/ GB.Link.Server/
RUN dotnet publish GB.Link.Server/GB.Link.Server.csproj -c Release -a $TARGETARCH --no-restore --self-contained false -p:UseAppHost=false -o /out

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /out/ ./
ENV LINK_BIND=0.0.0.0 LINK_PORT=8766 DOTNET_EnableDiagnostics=0
USER $APP_UID
EXPOSE 8766/tcp
HEALTHCHECK --interval=30s --timeout=5s --start-period=5s --retries=3 CMD ["dotnet", "GB.Link.Server.dll", "--health-check"]
ENTRYPOINT ["dotnet", "GB.Link.Server.dll"]
