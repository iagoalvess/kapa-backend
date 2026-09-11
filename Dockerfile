# Um Dockerfile com vários alvos, em vez de um por projeto: as etapas de restore e build
# são compartilhadas, então a API e o worker reaproveitam o mesmo cache de camada.
#
#   docker build --target api        -t backend-api    .
#   docker build --target worker     -t backend-worker .
#   docker build --target migrations -t backend-migr   .

# ---------------------------------------------------------------------------
# Restore: copia só os arquivos de projeto primeiro.
# Assim o "dotnet restore" só reexecuta quando uma dependência muda de verdade,
# e não a cada linha de código alterada.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS restore
WORKDIR /origem

COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/Backend.Api/Backend.Api.csproj             src/Backend.Api/
COPY src/Backend.Business/Backend.Business.csproj   src/Backend.Business/
COPY src/Backend.Data/Backend.Data.csproj           src/Backend.Data/
COPY src/Backend.Worker/Backend.Worker.csproj       src/Backend.Worker/

RUN dotnet restore src/Backend.Api/Backend.Api.csproj \
 && dotnet restore src/Backend.Worker/Backend.Worker.csproj

# ---------------------------------------------------------------------------
# Build
# ---------------------------------------------------------------------------
FROM restore AS build
COPY src/ src/

RUN dotnet publish src/Backend.Api/Backend.Api.csproj       -c Release -o /publicado/api    --no-restore \
 && dotnet publish src/Backend.Worker/Backend.Worker.csproj -c Release -o /publicado/worker --no-restore

# ---------------------------------------------------------------------------
# Migrations: imagem separada, executada como passo de deploy.
# Não roda no boot da API — ver docs/decisoes.md, item 5.
# ---------------------------------------------------------------------------
FROM restore AS migrations
WORKDIR /origem

# Parte da etapa "restore", e não de uma imagem limpa: o "dotnet ef" precisa do
# project.assets.json, que só existe depois do restore. Imagem limpa com "COPY . ."
# falha com NETSDK1004 porque o .dockerignore exclui obj/.
COPY .config/ .config/
COPY src/ src/

RUN dotnet tool restore

ENTRYPOINT ["dotnet", "ef", "database", "update", \
            "--project", "src/Backend.Data", \
            "--startup-project", "src/Backend.Api"]

# ---------------------------------------------------------------------------
# Runtime da API
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS api
WORKDIR /app

# As imagens Alpine vêm enxutas e ligam a globalização invariante por padrão. Sem estes
# três pacotes o container quebra em coisas que passam despercebidas no Windows:
#   icu-libs    comparação e formatação sensíveis a cultura (pt-BR)
#   tzdata      TimeZoneInfo.FindSystemTimeZoneById — sem ele, DataUtils lança
#   krb5-libs   o Npgsql carrega GSSAPI ao abrir conexão e loga erro sem ele
RUN apk add --no-cache icu-libs tzdata krb5-libs

# O diretório precisa existir e pertencer ao "app" ANTES de o volume ser montado: o Docker
# copia o dono do diretório da imagem para o volume novo. Sem isto, o volume nasce de root e
# o processo — que não roda como root — leva "Permission denied" no primeiro upload.
RUN mkdir -p /app/arquivos && chown -R app:app /app/arquivos

# A imagem já traz o usuário sem privilégios "app" (uid 1654). Container de aplicação
# rodando como root transforma uma falha de execução remota em comprometimento do host.
USER app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=1 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

EXPOSE 8080
COPY --from=build /publicado/api ./
ENTRYPOINT ["dotnet", "Backend.Api.dll"]

# ---------------------------------------------------------------------------
# Runtime do worker
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS worker
WORKDIR /app

RUN apk add --no-cache icu-libs tzdata krb5-libs

USER app
ENV DOTNET_gcServer=1 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

COPY --from=build /publicado/worker ./
ENTRYPOINT ["dotnet", "Backend.Worker.dll"]
