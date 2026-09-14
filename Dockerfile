# Render (and most container-based hosts) have no native .NET runtime — this
# builds the ASP.NET Core API and packages it together with the static
# frontend (index.html/manifest.json/icons) so one container serves both,
# same-origin, matching how Program.cs locates the frontend
# (ContentRootPath/../..) in local `dotnet run` too.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY backend/BiTanEnergyApi/BiTanEnergyApi.csproj backend/BiTanEnergyApi/
RUN dotnet restore backend/BiTanEnergyApi/BiTanEnergyApi.csproj
COPY backend/BiTanEnergyApi/ backend/BiTanEnergyApi/
RUN dotnet publish backend/BiTanEnergyApi/BiTanEnergyApi.csproj -c Release -o /app/backend/BiTanEnergyApi

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app/backend/BiTanEnergyApi
COPY --from=build /app/backend/BiTanEnergyApi ./
COPY index.html manifest.json /app/
COPY icons/ /app/icons/

ENTRYPOINT ["dotnet", "BiTanEnergyApi.dll"]
