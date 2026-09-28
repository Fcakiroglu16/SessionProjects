FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["ObservabilityAgent/ObservabilityAgent.csproj", "ObservabilityAgent/"]
COPY ["SessionProjects.Agents.Common/SessionProjects.Agents.Common.csproj", "SessionProjects.Agents.Common/"]
RUN dotnet restore "ObservabilityAgent/ObservabilityAgent.csproj"
COPY . .
WORKDIR "/src/ObservabilityAgent"
RUN dotnet publish "./ObservabilityAgent.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ObservabilityAgent.dll"]
