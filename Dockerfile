FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 8443

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["NoviSad.SokoBot.csproj", "./"]
RUN dotnet restore "NoviSad.SokoBot.csproj"
COPY . .
WORKDIR "/src/"
RUN dotnet build "NoviSad.SokoBot.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "NoviSad.SokoBot.csproj" -c Release -o /app/publish

WORKDIR /app
COPY /sokobot.pem /sokobot.pem
COPY /sokobot.pfx /sokobot.pfx

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "NoviSad.SokoBot.dll"]
