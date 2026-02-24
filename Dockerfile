FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore
COPY src/DonetickCalDav/DonetickCalDav.csproj ./DonetickCalDav/
RUN dotnet restore ./DonetickCalDav/DonetickCalDav.csproj

# Copy source and publish
COPY src/DonetickCalDav/ ./DonetickCalDav/
RUN dotnet publish ./DonetickCalDav/DonetickCalDav.csproj -c Release -o /app --no-restore

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

EXPOSE 5232

ENV ASPNETCORE_URLS=http://+:5232
ENV Donetick__BaseUrl=http://localhost:8080
ENV Donetick__ApiKey=
ENV Donetick__PollIntervalSeconds=30
ENV CalDav__Username=user
ENV CalDav__Password=pass
ENV CalDav__CalendarName=Donetick Tasks
ENV CalDav__CalendarColor=#4A90D9FF
ENV CalDav__ListenPort=5232

ENTRYPOINT ["dotnet", "DonetickCalDav.dll"]
