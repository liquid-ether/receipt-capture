# Receipt Capture — self-hosted web app.
#
# The runtime image carries Tesseract with English and French, because OCR runs on
# the server. Keeping it in the image means the app has no CDN or third-party
# service to depend on at runtime: a receipt photo goes to your server and nowhere
# else.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props ./
COPY src/ReceiptCapture.Core/ReceiptCapture.Core.csproj src/ReceiptCapture.Core/
COPY src/ReceiptCapture.Web/ReceiptCapture.Web.csproj src/ReceiptCapture.Web/
RUN dotnet restore src/ReceiptCapture.Web/ReceiptCapture.Web.csproj

COPY src/ src/
RUN dotnet publish src/ReceiptCapture.Web/ReceiptCapture.Web.csproj \
        --configuration Release \
        --no-restore \
        --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# tesseract-ocr: the OCR engine and its English and French language data.
# libsqlite3-0: the app binds to the system SQLite rather than shipping its own.
RUN apt-get update \
    && apt-get install --no-install-recommends --yes \
        tesseract-ocr \
        tesseract-ocr-eng \
        tesseract-ocr-fra \
        libsqlite3-0 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

# Receipts, photos and the data-protection key ring. Mount a volume here, or a
# container restart loses every receipt that has not been sent yet.
ENV ReceiptCapture__DataDirectory=/data
VOLUME ["/data"]

# Run as the non-root user the base image provides.
USER $APP_UID

EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

ENTRYPOINT ["dotnet", "ReceiptCapture.Web.dll"]
