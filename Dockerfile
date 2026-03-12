# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["Hounded_Herart.sln", "./"]
COPY ["Hounded_Heart.Api/Hounded_Heart.Api.csproj", "Hounded_Heart.Api/"]
COPY ["Hounded_Heart.Models/Hounded_Heart.Models.csproj", "Hounded_Heart.Models/"]
COPY ["Hounded_Heart.Services/Hounded_Heart.Services.csproj", "Hounded_Heart.Services/"]

# Restore dependencies
RUN dotnet restore "Hounded_Herart.sln"

# Copy all source files
COPY . .

# Build and publish the API project
WORKDIR "/src/Hounded_Heart.Api"
RUN dotnet publish "Hounded_Heart.Api.csproj" -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Copy published output from build stage
COPY --from=build /app/publish .

# Expose port
EXPOSE 8080
EXPOSE 443

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080

# Run the application
ENTRYPOINT ["dotnet", "Hounded_Heart.Api.dll"]
