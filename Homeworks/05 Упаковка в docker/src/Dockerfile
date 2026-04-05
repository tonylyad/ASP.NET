FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY PromoCodeFactory.Core/PromoCodeFactory.Core.csproj PromoCodeFactory.Core/
COPY PromoCodeFactory.DataAccess/PromoCodeFactory.DataAccess.csproj PromoCodeFactory.DataAccess/
COPY PromoCodeFactory.WebHost/PromoCodeFactory.WebHost.csproj PromoCodeFactory.WebHost/
COPY PromoCodeFactory.UnitTests/PromoCodeFactory.UnitTests.csproj PromoCodeFactory.UnitTests/

RUN dotnet restore PromoCodeFactory.WebHost/PromoCodeFactory.WebHost.csproj

COPY . .

RUN dotnet build PromoCodeFactory.WebHost/PromoCodeFactory.WebHost.csproj -c Release -o /app/build

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/build .

ENTRYPOINT ["dotnet", "PromoCodeFactory.WebHost.dll"]