FROM mcr.microsoft.com/dotnet/sdk:11.0-preview AS build
WORKDIR /src

ARG GIT_SHA=dev-local
ARG BUILD_DATE=local
ARG IMAGE_TAG=dev-local

COPY MediaCloud.slnx ./
COPY apps/api/api.csproj apps/api/
COPY apps/web/web.csproj apps/web/
RUN dotnet restore MediaCloud.slnx

COPY . .
RUN dotnet publish apps/api/api.csproj -c Release -o /app/publish/api /p:UseAppHost=false
RUN dotnet publish apps/web/web.csproj -c Release -o /app/publish/web /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:11.0-preview AS runtime
WORKDIR /app

ARG GIT_SHA=dev-local
ARG BUILD_DATE=local
ARG IMAGE_TAG=dev-local

RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish/api ./api
COPY --from=build /app/publish/web ./web
COPY docker/start.sh /app/start.sh
RUN chmod +x /app/start.sh && mkdir -p /app/data

ENV API_PORT=5299
ENV WEB_PORT=8080
ENV ApiBaseUrl=http://127.0.0.1:5299
ENV ConnectionStrings__DefaultConnection=Data Source=/app/data/mediacloud.db
ENV BuildInfo__GitSha=$GIT_SHA
ENV BuildInfo__BuildTimestampUtc=$BUILD_DATE
ENV BuildInfo__ImageTag=$IMAGE_TAG

EXPOSE 8080
VOLUME ["/app/data"]

ENTRYPOINT ["/app/start.sh"]
