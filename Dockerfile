# syntax=docker/dockerfile:1

FROM node:22-alpine AS web
WORKDIR /web
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/src/LifeCounterAssist.Api/LifeCounterAssist.Api.csproj src/LifeCounterAssist.Api/
RUN dotnet restore src/LifeCounterAssist.Api/LifeCounterAssist.Api.csproj
COPY backend/src/LifeCounterAssist.Api/ src/LifeCounterAssist.Api/
RUN dotnet publish src/LifeCounterAssist.Api/LifeCounterAssist.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
COPY --from=web /web/dist ./wwwroot
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "LifeCounterAssist.Api.dll"]
