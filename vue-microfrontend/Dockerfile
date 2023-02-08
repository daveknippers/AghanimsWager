# build root-config
FROM node:latest as build-rootconfig
WORKDIR /app
COPY aghanims-wager-spa-root-config/package*.json ./
RUN npm install
COPY aghanims-wager-spa-root-config/ .
RUN npm run build

# build root-config
FROM node:latest as build-vueapp
WORKDIR /vue-app
COPY aghanims-wager-spa-vue-app/package*.json ./
RUN npm install
COPY aghanims-wager-spa-vue-app/ .
RUN npm run build

# production stage
FROM nginx:stable-alpine as production-stage
COPY --from=build-rootconfig /app/dist /usr/share/nginx/html
COPY --from=build-vueapp /vue-app/dist/js /usr/share/nginx/html/js
COPY --from=build-vueapp /vue-app/dist/favicon.ico /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY nginx.conf /etc/nginx/sites-available/aghanimswager.com
COPY nginx.conf /etc/nginx/sites-enabled/aghanimswager.com
EXPOSE 80
EXPOSE 443
CMD ["nginx", "-g", "daemon off;"]