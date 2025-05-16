# Use ASP.NET Core runtime as base image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80

# Use SDK image to build the app
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files
COPY . .

# Restore and build
RUN dotnet restore "./GittBilSmsCore.csproj"
RUN dotnet publish "./GittBilSmsCore.csproj" -c Release -o /app/publish

# Final stage
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .

# Start the application
ENTRYPOINT ["dotnet", "GittBilSmsCore.dll"]
