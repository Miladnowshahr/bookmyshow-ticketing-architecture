FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY BookMyShowTicketing.sln .
COPY src/BookingApi/BookingApi.csproj src/BookingApi/
COPY src/BookingApi.Domain/BookingApi.Domain.csproj src/BookingApi.Domain/
COPY src/BookingApi.Infrastructure/BookingApi.Infrastructure.csproj src/BookingApi.Infrastructure/
COPY src/BookingApi.Sagas/BookingApi.Sagas.csproj src/BookingApi.Sagas/
COPY src/BookingApi.Contracts/BookingApi.Contracts.csproj src/BookingApi.Contracts/
COPY tests/BookingApi.Tests/BookingApi.Tests.csproj tests/BookingApi.Tests/
RUN dotnet restore src/BookingApi/BookingApi.csproj

COPY src/ src/
RUN dotnet publish src/BookingApi/BookingApi.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "BookingApi.dll"]
