FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["GiftCardEngineServer/GiftCardEngineServer.csproj", "GiftCardEngineServer/"]
RUN dotnet restore "./GiftCardEngineServer/GiftCardEngineServer.csproj"
COPY . .
WORKDIR "/src/GiftCardEngineServer"
RUN dotnet build "./GiftCardEngineServer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "./GiftCardEngineServer.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY GiftCardEngineServer/catalogue.json .
ENTRYPOINT ["dotnet", "GiftCardEngineServer.dll"]
