# The processing host with every rendering tool; build after `dotnet publish src/Shareme.Processing.Host -c Release -o publish/processing`:
#   docker build -f src/Shareme.Processing.Host/Dockerfile -t shareme-processing publish/processing
FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg ghostscript libimage-exiftool-perl libreoffice-writer-nogui libreoffice-calc-nogui libreoffice-impress-nogui fonts-dejavu fonts-liberation \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY . .
USER app
ENTRYPOINT ["dotnet", "Shareme.Processing.Host.dll"]
