FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["CaseSummaryAgent/CaseSummaryAgent.csproj", "CaseSummaryAgent/"]
COPY ["SessionProjects.Contracts/SessionProjects.Contracts.csproj", "SessionProjects.Contracts/"]
RUN dotnet restore "CaseSummaryAgent/CaseSummaryAgent.csproj"
COPY . .
WORKDIR "/src/CaseSummaryAgent"
RUN dotnet publish "./CaseSummaryAgent.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "CaseSummaryAgent.dll"]
