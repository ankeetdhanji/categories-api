FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["src/CategoriesBackend/CategoriesBackend.csproj", "CategoriesBackend/"]
COPY ["src/CategoriesBackend.Core/CategoriesBackend.Core.csproj", "CategoriesBackend.Core/"]
COPY ["src/CategoriesBackend.Infrastructure/CategoriesBackend.Infrastructure.csproj", "CategoriesBackend.Infrastructure/"]
RUN dotnet restore "CategoriesBackend/CategoriesBackend.csproj"

COPY src/ .
WORKDIR "/src/CategoriesBackend"
RUN dotnet build "CategoriesBackend.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "CategoriesBackend.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CategoriesBackend.dll"]
