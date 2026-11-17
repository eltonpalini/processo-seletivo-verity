FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["CashFlow.sln", "./"]
COPY ["src/CashFlow.Domain/CashFlow.Domain.csproj", "src/CashFlow.Domain/"]
COPY ["src/CashFlow.Application/CashFlow.Application.csproj", "src/CashFlow.Application/"]
COPY ["src/CashFlow.Infrastructure/CashFlow.Infrastructure.csproj", "src/CashFlow.Infrastructure/"]
COPY ["src/CashFlow.ConsolidationWorker/CashFlow.ConsolidationWorker.csproj", "src/CashFlow.ConsolidationWorker/"]

RUN dotnet restore "src/CashFlow.ConsolidationWorker/CashFlow.ConsolidationWorker.csproj"

COPY ["src/CashFlow.Domain/", "src/CashFlow.Domain/"]
COPY ["src/CashFlow.Application/", "src/CashFlow.Application/"]
COPY ["src/CashFlow.Infrastructure/", "src/CashFlow.Infrastructure/"]
COPY ["src/CashFlow.ConsolidationWorker/", "src/CashFlow.ConsolidationWorker/"]

RUN dotnet publish "src/CashFlow.ConsolidationWorker/CashFlow.ConsolidationWorker.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "CashFlow.ConsolidationWorker.dll"]
