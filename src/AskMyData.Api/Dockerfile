# Build context is the repository root:
#   docker build -f src/AskMyData.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first so the layer is cached until a project file changes.
COPY Directory.Build.props ./
COPY src/AskMyData.Domain/AskMyData.Domain.csproj src/AskMyData.Domain/
COPY src/AskMyData.Application/AskMyData.Application.csproj src/AskMyData.Application/
COPY src/AskMyData.Infrastructure/AskMyData.Infrastructure.csproj src/AskMyData.Infrastructure/
COPY src/AskMyData.Api/AskMyData.Api.csproj src/AskMyData.Api/
RUN dotnet restore src/AskMyData.Api/AskMyData.Api.csproj

COPY src/ src/
RUN dotnet publish src/AskMyData.Api/AskMyData.Api.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "AskMyData.Api.dll"]
