# syntax=docker/dockerfile:1.7
FROM node:20.12.2-alpine3.19 AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN --mount=type=cache,target=/root/.npm npm ci --no-audit --no-fund
COPY . .
RUN npx ng build --configuration production

FROM nginx:1.25.2-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/portal/browser /usr/share/nginx/html
# run as the unprivileged nginx user on a high port
RUN chown -R nginx:nginx /var/cache/nginx /usr/share/nginx/html && touch /var/run/nginx.pid && chown nginx:nginx /var/run/nginx.pid
USER nginx
EXPOSE 8080
