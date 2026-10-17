FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy source and restore+publish in one step, so package restore and
# publish never straddle a Docker layer boundary
COPY src/DonetickCalDav/ ./DonetickCalDav/
RUN dotnet publish ./DonetickCalDav/DonetickCalDav.csproj -c Release -o /app

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

EXPOSE 5232

# ENV ASPNETCORE_URLS=http://+:5232
# ENV Donetick__BaseUrl=http://localhost:8080
# ENV Donetick__ApiKey=
# ENV Donetick__PollIntervalSeconds=30
# ENV CalDav__CalendarName=DonetickTasks
# ENV CalDav__CalendarColor=#4A90D9FF
# ENV CalDav__GroupByLabel=false
# ENV CalDav__DefaultCalendarName=General
# ENV CalDav__AllDayEvents=false
# ENV CalDav__PreserveScheduledTime=false
# ENV CalDav__ListenPort=5232

ENTRYPOINT ["dotnet", "DonetickCalDav.dll"]
