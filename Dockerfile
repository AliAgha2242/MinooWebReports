# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["MinooWebReports.csproj", "./"]
RUN dotnet restore "MinooWebReports.csproj"

COPY . .
RUN dotnet publish "MinooWebReports.csproj" \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

COPY --from=build /app/publish .

RUN mkdir -p /app/Logs

ENTRYPOINT ["dotnet", "MinooWebReports.dll"]
