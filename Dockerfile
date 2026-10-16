# Filament-Verwaltung Server
#   docker build -t filament-server .
#   docker run -d --name filament-server -p 8090:8080 -v /mnt/user/appdata/filament:/data filament-server
# Datenbank, Dateien, Sicherungen und token.txt liegen im Volume /data.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY FilamentVerwaltung.Kern/FilamentVerwaltung.Kern.vbproj FilamentVerwaltung.Kern/
COPY FilamentVerwaltung.Server/FilamentVerwaltung.Server.vbproj FilamentVerwaltung.Server/
RUN dotnet restore FilamentVerwaltung.Server/FilamentVerwaltung.Server.vbproj
COPY FilamentVerwaltung.Kern/ FilamentVerwaltung.Kern/
COPY FilamentVerwaltung.Server/ FilamentVerwaltung.Server/
RUN dotnet publish FilamentVerwaltung.Server/FilamentVerwaltung.Server.vbproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# ffmpeg für das Kamerabild der Bambu-Drucker X1/H2D (RTSPS)
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
# Unraid legt appdata-Ordner meist als root an – deshalb als root laufen.
# Wer lieber nobody:users nutzt: in Unraid unter "Extra Parameters" --user 99:100 eintragen (Ordner muss 99:100 gehören).
USER root
ENV FILAMENT_DATEN=/data \
    ASPNETCORE_HTTP_PORTS=8080 \
    TZ=Europe/Berlin
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "FilamentVerwaltung.Server.dll"]
