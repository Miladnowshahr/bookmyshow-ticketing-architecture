FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY gateway/YarpGateway/YarpGateway.csproj gateway/YarpGateway/
RUN dotnet restore gateway/YarpGateway/YarpGateway.csproj

COPY gateway/YarpGateway/ gateway/YarpGateway/
RUN dotnet publish gateway/YarpGateway/YarpGateway.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "YarpGateway.dll"]
