# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY AIM.Web.csproj ./
RUN dotnet restore AIM.Web.csproj
COPY . .
RUN dotnet publish AIM.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Npgsql probes for libgssapi_krb5.so.2 on every connection open (Kerberos
# auth negotiation). The .NET runtime image is Debian slim and doesn't ship
# it, so every DB open fails with:
#   "libgssapi_krb5.so.2: cannot open shared object file: No such file or directory"
# Installing libgssapi-krb5-2 (~2 MB) lets Npgsql find the symbol, probe, and
# fall back cleanly when the server advertises password auth (which Neon does).
RUN apt-get update \
 && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
 && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "AIM.Web.dll"]
