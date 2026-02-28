# syntax=docker/dockerfile:1
FROM node:22-alpine

WORKDIR /app

# Copy app source (no external dependencies)
COPY . .

ENV NODE_ENV=production
EXPOSE 8099

CMD ["node", "server.mjs"]
