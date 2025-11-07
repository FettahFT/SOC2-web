# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files
COPY ["ShadeOfColor2/ShadeOfColor2.csproj", "ShadeOfColor2/"]
COPY ["ShadeOfColor2.sln", "./"]

# Restore packages
RUN dotnet restore "ShadeOfColor2/ShadeOfColor2.csproj"

# Copy everything else
COPY . .

# Publish
RUN dotnet publish "ShadeOfColor2/ShadeOfColor2.csproj" \
    -c Release \
    -o /app/publish

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

ENTRYPOINT ["dotnet", "ShadeOfColor2.dll"]