# Build frontend
FROM node:18 AS frontend-build
WORKDIR /app
COPY soc-frontend/package*.json ./
RUN npm ci
COPY soc-frontend/ ./
RUN npm run build

# Build backend
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS backend-build
WORKDIR /src

# Copy source files (excluding bin/obj via .dockerignore)
COPY . .

# Restore and publish in one step to avoid Windows path issues
RUN dotnet publish "SOC2-Web/ShadeOfColor2.API/ShadeOfColor2.API.csproj" \
    -c Release \
    -o /app/publish \
    --verbosity minimal

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Install native dependencies for ImageSharp if needed
RUN apt-get update && apt-get install -y \
    libgdiplus \
    && rm -rf /var/lib/apt/lists/*

COPY --from=backend-build /app/publish .
COPY --from=frontend-build /app/build ./wwwroot

EXPOSE 80
ENV ASPNETCORE_URLS=http://+:80

ENTRYPOINT ["dotnet", "ShadeOfColor2.API.dll"]