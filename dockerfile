# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy source files (excluding bin/obj via .dockerignore)
COPY . .

# Restore and publish in one step to avoid Windows path issues
RUN dotnet publish "ShadeOfColor2.API/ShadeOfColor2.API.csproj" \
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

COPY --from=build /app/publish .
EXPOSE 80
ENV ASPNETCORE_URLS=http://+:80

ENTRYPOINT ["dotnet", "ShadeOfColor2.API.dll"]