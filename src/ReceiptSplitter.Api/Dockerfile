# The API image. Build from the repo root, since the API needs Core and samples/:
#   docker build -f src/ReceiptSplitter.Api/Dockerfile -t receipt-splitter-api .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Restore first, so the package layer is cached until a project file changes.
COPY global.json Directory.Build.props ./
COPY src/ReceiptSplitter.Core/ReceiptSplitter.Core.csproj src/ReceiptSplitter.Core/
COPY src/ReceiptSplitter.Api/ReceiptSplitter.Api.csproj src/ReceiptSplitter.Api/
RUN dotnet restore src/ReceiptSplitter.Api/ReceiptSplitter.Api.csproj

COPY src/ReceiptSplitter.Core/ src/ReceiptSplitter.Core/
COPY src/ReceiptSplitter.Api/ src/ReceiptSplitter.Api/
COPY samples/ samples/
RUN dotnet publish src/ReceiptSplitter.Api/ReceiptSplitter.Api.csproj \
    --configuration Release --no-restore --output /app -p:UseAppHost=false

# Chiseled: no shell or package manager, and runs as a non-root user.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "ReceiptSplitter.Api.dll"]
