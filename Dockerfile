FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY building-blocks/ building-blocks/
COPY src/ src/
RUN dotnet restore src/Doacoes.Worker/Doacoes.Worker.csproj
RUN dotnet publish src/Doacoes.Worker/Doacoes.Worker.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Doacoes.Worker.dll"]
