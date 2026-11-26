# syntax=docker/dockerfile:1

FROM node:22-alpine AS build
WORKDIR /src
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.27-alpine AS runtime
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY docker/40-app-config.sh /docker-entrypoint.d/40-app-config.sh
COPY --from=build /src/dist /usr/share/nginx/html
USER root
# config.js is generated at startup by the non-root nginx user.
RUN chmod +x /docker-entrypoint.d/40-app-config.sh \
    && chown nginx /usr/share/nginx/html /usr/share/nginx/html/config.js
USER nginx
EXPOSE 8080
