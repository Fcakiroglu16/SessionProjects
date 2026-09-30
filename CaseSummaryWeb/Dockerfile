FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["CaseSummaryWeb/CaseSummaryWeb/CaseSummaryWeb.csproj", "CaseSummaryWeb/CaseSummaryWeb/"]
COPY ["CaseSummaryWeb/CaseSummaryWeb.Client/CaseSummaryWeb.Client.csproj", "CaseSummaryWeb/CaseSummaryWeb.Client/"]
COPY ["SessionProjects.ServiceDefaults/SessionProjects.ServiceDefaults.csproj", "SessionProjects.ServiceDefaults/"]
RUN dotnet restore "CaseSummaryWeb/CaseSummaryWeb/CaseSummaryWeb.csproj"
COPY . .
WORKDIR "/src/CaseSummaryWeb/CaseSummaryWeb"
RUN dotnet publish "./CaseSummaryWeb.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "CaseSummaryWeb.dll"]
