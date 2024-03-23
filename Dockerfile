FROM mcr.microsoft.com/dotnet/aspnet:7.0-alpine3.17 AS base
RUN apk update && \
  apk upgrade && \
  apk add --update ca-certificates && \
  apk add chromium --update-cache --repository http://nl.alpinelinux.org/alpine/edge/community \
  rm -rf /var/cache/apk/*
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:7.0-alpine3.17 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Server.csproj", "./"]
RUN dotnet restore "Server.csproj"
COPY . .
WORKDIR "/src/"
RUN dotnet build "Server.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Server.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Server.dll"]
