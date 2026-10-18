FROM --platform=$BUILDPLATFORM debian:bookworm-slim AS frp
ARG TARGETARCH
ARG FRP_VERSION=v0.71.2
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates curl tar && rm -rf /var/lib/apt/lists/* && \
    case "$TARGETARCH" in \
      amd64) asset=LoliaFrp_linux_amd64.tar.gz; digest=b8b8376cf63d382a02a55699debf804bd39b064b31d5b847595d7f7e80777c73 ;; \
      arm64) asset=LoliaFrp_linux_arm64.tar.gz; digest=e762fd5f61e726352a88f9bb109311daad7959ad176a2cdba62a0dd915f8eca1 ;; \
      *) echo "Unsupported architecture: $TARGETARCH"; exit 1 ;; \
    esac && \
    curl --fail --location --retry 3 "https://github.com/Lolia-FRP/lolia-frp/releases/download/${FRP_VERSION}/${asset}" -o /tmp/frp.tar.gz && \
    echo "$digest  /tmp/frp.tar.gz" | sha256sum --check && \
    entry="$(tar -tzf /tmp/frp.tar.gz | awk '$NF == "frpc" {print; exit}')" && test -n "$entry" && \
    tar -xOf /tmp/frp.tar.gz "$entry" > /frpc && chmod 755 /frpc && /frpc --version

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY Directory.Build.props ./
COPY Lolia.Shared/ Lolia.Shared/
COPY Lolia.Web/ Lolia.Web/
RUN case "$TARGETARCH" in amd64) rid=linux-x64 ;; arm64) rid=linux-arm64 ;; *) echo "Unsupported architecture: $TARGETARCH"; exit 1 ;; esac && \
    dotnet publish Lolia.Web/Lolia.Web.csproj -c Release -r "$rid" --self-contained false -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ENV ASPNETCORE_URLS=http://+:8080 LOLIABLOOM_DATA=/data DOTNET_EnableDiagnostics=0
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates curl && rm -rf /var/lib/apt/lists/* && \
    mkdir -p /data /app && chown -R 1654:1654 /data /app
WORKDIR /app
COPY --from=build --chown=1654:1654 /out/ ./
COPY --from=frp --chown=1654:1654 /frpc /app/frpc
COPY --chown=1654:1654 LICENSE THIRD-PARTY-NOTICES.md /app/
USER 1654:1654
EXPOSE 8080
VOLUME ["/data"]
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 CMD curl --fail --silent http://127.0.0.1:8080/health || exit 1
ENTRYPOINT ["dotnet", "Lolia.Web.dll"]
