FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["BayrolConnect/BayrolConnect.csproj", "BayrolConnect/"]
COPY ["BayrolLib/BayrolLib.csproj", "BayrolLib/"]
RUN dotnet restore "BayrolConnect/BayrolConnect.csproj"
COPY . .
WORKDIR "/src/BayrolConnect"
RUN dotnet build "BayrolConnect.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "BayrolConnect.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BayrolConnect.dll"]
