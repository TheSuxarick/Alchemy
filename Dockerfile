FROM nginx:alpine

ARG WEBGL_BUILD_DIR=build/WebGL/Alchemy

COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY ${WEBGL_BUILD_DIR}/ /usr/share/nginx/html/

EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
