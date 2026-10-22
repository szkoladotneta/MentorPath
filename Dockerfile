# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/MentorPath.Data/MentorPath.Data.csproj src/MentorPath.Data/
COPY src/MentorPath.Application/MentorPath.Application.csproj src/MentorPath.Application/
COPY src/MentorPath.Web/MentorPath.Web.csproj src/MentorPath.Web/
RUN dotnet restore src/MentorPath.Web/MentorPath.Web.csproj
COPY src/ src/
# Restore again once sources are present: the SDK adds the implicit Blazor framework
# assets package (blazor.web.js) only when it sees Razor components.
RUN dotnet publish src/MentorPath.Web/MentorPath.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DataProtection__KeysPath=/keys \
    DOTNET_RUNNING_IN_CONTAINER=true
RUN mkdir -p /keys && chown $APP_UID /keys
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "MentorPath.Web.dll"]
