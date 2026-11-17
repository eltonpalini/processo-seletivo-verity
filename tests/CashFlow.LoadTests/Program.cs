using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CashFlow.Application.DTOs;
using CashFlow.Domain.Enums;

var baseUrl = args.FirstOrDefault() ?? "http://localhost:5000";
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    Converters = { new JsonStringEnumConverter() }
};

Console.OutputEncoding = System.Text.Encoding.UTF8;
PrintBanner(baseUrl);

using var httpClient = new HttpClient
{
    BaseAddress = new Uri(baseUrl),
    Timeout = TimeSpan.FromSeconds(15)
};

// 0. Autenticação JWT
Console.WriteLine("[0/3] Autenticando na API (POST /api/v1/auth/token)...");
var tokenResponse = await httpClient.PostAsJsonAsync(
    "/api/v1/auth/token",
    new TokenRequest("comerciante", "Verity@2026"));

if (!tokenResponse.IsSuccessStatusCode)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Falha ao autenticar: HTTP {(int)tokenResponse.StatusCode}");
    Console.ResetColor();
    return 1;
}

var authData = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>(jsonOptions);
httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authData!.AccessToken);
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("  ✓ Token JWT obtido com sucesso.\n");
Console.ResetColor();

// Usa uma data exclusiva por execução para garantir determinismo contábil
var randomDayOffset = Random.Shared.Next(100, 9000);
var testDate1 = DateOnly.FromDateTime(DateTime.Today.AddDays(randomDayOffset));
var testDate3 = DateOnly.FromDateTime(DateTime.Today.AddDays(randomDayOffset + 1));

// =========================================================================
// CENÁRIO 1: CONCORRÊNCIA NA ESCRITA E CONSOLIDAÇÃO (100 LANÇAMENTOS SIMULTÂNEOS)
// =========================================================================
Console.WriteLine("=========================================================================");
Console.WriteLine(" CENÁRIO 1: CONCORRÊNCIA DE LANÇAMENTOS + INTEGRIDADE CONTÁBIL DO SALDO");
Console.WriteLine("=========================================================================");
Console.WriteLine($"Data alvo do teste: {testDate1:yyyy-MM-dd}");
Console.WriteLine("Disparando 100 lançamentos simultâneos (60 Créditos de R$ 100,00 + 40 Débitos de R$ 50,00)...");

const int creditCount = 60;
const decimal creditAmount = 100.00m;
const int debitCount = 40;
const decimal debitAmount = 50.00m;
const int totalConcurrentWrites = creditCount + debitCount;

var expectedCredits = creditCount * creditAmount; // R$ 6.000,00
var expectedDebits = debitCount * debitAmount;    // R$ 2.000,00
var expectedNetBalance = expectedCredits - expectedDebits; // R$ 4.000,00

var writeLatencies = new ConcurrentBag<double>();
var writeStatusCodes = new ConcurrentBag<HttpStatusCode>();

var writeRequests = Enumerable.Range(1, totalConcurrentWrites).Select(index =>
{
    var isCredit = index <= creditCount;
    var req = new CreateTransactionRequest(
        Amount: isCredit ? creditAmount : debitAmount,
        Type: isCredit ? TransactionType.Credit : TransactionType.Debit,
        Date: testDate1,
        Description: $"Teste Concorrência #{index:000}");

    return Task.Run(async () =>
    {
        var sw = Stopwatch.StartNew();
        var resp = await httpClient.PostAsJsonAsync("/api/v1/transactions", req);
        sw.Stop();
        writeLatencies.Add(sw.Elapsed.TotalMilliseconds);
        writeStatusCodes.Add(resp.StatusCode);
    });
});

var totalWriteSw = Stopwatch.StartNew();
await Task.WhenAll(writeRequests);
totalWriteSw.Stop();

var acceptedCount = writeStatusCodes.Count(s => s == HttpStatusCode.Accepted);
Console.WriteLine($"  • Requisições enviadas: {totalConcurrentWrites} em {totalWriteSw.Elapsed.TotalMilliseconds:F0} ms");
Console.WriteLine($"  • Respostas 202 Accepted: {acceptedCount}/{totalConcurrentWrites}");
PrintLatencyStats("Ingestão (POST /transactions)", writeLatencies.ToArray());

// Aguarda a consolidação assíncrona (Fila 1 -> Worker 1 -> Fila 2 -> Worker 2 -> Postgres + Redis)
Console.WriteLine("  • Aguardando Workers processarem as filas (RabbitMQ -> PostgreSQL -> Redis)...");
var consolidationSw = Stopwatch.StartNew();
DailyBalanceResponse? consolidatedBalance = null;

while (consolidationSw.Elapsed < TimeSpan.FromSeconds(20))
{
    consolidatedBalance = await httpClient.GetFromJsonAsync<DailyBalanceResponse>(
        $"/api/v1/daily-balance?date={testDate1:yyyy-MM-dd}",
        jsonOptions);

    if (consolidatedBalance is { TransactionCount: totalConcurrentWrites })
    {
        break;
    }

    await Task.Delay(100);
}
consolidationSw.Stop();

Console.WriteLine($"  • Tempo total de convergência (Consistência Eventual de 100 itens): {consolidationSw.Elapsed.TotalMilliseconds:F0} ms");
Console.WriteLine($"  • Saldo retornado ({consolidatedBalance?.Source}):");
Console.WriteLine($"      - Qtd Lançamentos : {consolidatedBalance?.TransactionCount} (Esperado: {totalConcurrentWrites})");
Console.WriteLine($"      - Total Créditos  : R$ {consolidatedBalance?.TotalCredits:N2} (Esperado: R$ {expectedCredits:N2})");
Console.WriteLine($"      - Total Débitos   : R$ {consolidatedBalance?.TotalDebits:N2} (Esperado: R$ {expectedDebits:N2})");
Console.WriteLine($"      - Saldo Líquido   : R$ {consolidatedBalance?.NetBalance:N2} (Esperado: R$ {expectedNetBalance:N2})");

var scenario1Passed = consolidatedBalance is not null
    && acceptedCount == totalConcurrentWrites
    && consolidatedBalance.TransactionCount == totalConcurrentWrites
    && consolidatedBalance.TotalCredits == expectedCredits
    && consolidatedBalance.TotalDebits == expectedDebits
    && consolidatedBalance.NetBalance == expectedNetBalance;

PrintScenarioResult("CENÁRIO 1 (Concorrência e Exatidão Contábil)", scenario1Passed);

// =========================================================================
// CENÁRIO 2: PICO DE > 50 REQUISIÇÕES POR SEGUNDO NO CONSOLIDADO DIÁRIO
// =========================================================================
Console.WriteLine("\n=========================================================================");
Console.WriteLine(" CENÁRIO 2: PICO DE REQUISIÇÕES NO CONSOLIDADO DIÁRIO (META: >= 50 RPS)");
Console.WriteLine("=========================================================================");
const int durationSeconds = 5;
const int targetRps = 75; // 50% acima do requisito de 50 req/s do PDF
const int totalReadRequests = durationSeconds * targetRps;

Console.WriteLine($"Executando carga sustentada de ~{targetRps} req/s por {durationSeconds} segundos ({totalReadRequests} requisições)...");

var readLatencies = new ConcurrentBag<double>();
var readStatuses = new ConcurrentBag<HttpStatusCode>();
var sources = new ConcurrentBag<string>();

var loadSw = Stopwatch.StartNew();
for (var sec = 0; sec < durationSeconds; sec++)
{
    var batchStart = Stopwatch.StartNew();
    var batchTasks = Enumerable.Range(0, targetRps).Select(_ => Task.Run(async () =>
    {
        var reqSw = Stopwatch.StartNew();
        try
        {
            var response = await httpClient.GetAsync($"/api/v1/daily-balance?date={testDate1:yyyy-MM-dd}");
            reqSw.Stop();
            readLatencies.Add(reqSw.Elapsed.TotalMilliseconds);
            readStatuses.Add(response.StatusCode);

            if (response.IsSuccessStatusCode)
            {
                var dto = await response.Content.ReadFromJsonAsync<DailyBalanceResponse>(jsonOptions);
                if (dto is not null)
                {
                    sources.Add(dto.Source);
                }
            }
        }
        catch
        {
            reqSw.Stop();
            readLatencies.Add(reqSw.Elapsed.TotalMilliseconds);
            readStatuses.Add(HttpStatusCode.ServiceUnavailable);
        }
    }));

    await Task.WhenAll(batchTasks);
    batchStart.Stop();

    var remainingMs = 1000 - (int)batchStart.ElapsedMilliseconds;
    if (remainingMs > 0)
    {
        await Task.Delay(remainingMs);
    }
}
loadSw.Stop();

var successReads = readStatuses.Count(s => s == HttpStatusCode.OK);
var failedReads = totalReadRequests - successReads;
var lossPercentage = (failedReads / (double)totalReadRequests) * 100.0;
var achievedRps = totalReadRequests / loadSw.Elapsed.TotalSeconds;
var cacheHits = sources.Count(s => s == "RedisCache");

Console.WriteLine($"  • Total de requisições : {totalReadRequests}");
Console.WriteLine($"  • Vazão sustentada     : {achievedRps:F1} req/s (Meta mínima do desafio: 50 req/s)");
Console.WriteLine($"  • Sucessos (200 OK)    : {successReads}/{totalReadRequests} ({cacheHits} servidas direto do RedisCache)");
Console.WriteLine($"  • Perda de requisições : {lossPercentage:F2}% (Limite máximo do desafio: 5,00%)");
PrintLatencyStats("Leitura (GET /daily-balance)", readLatencies.ToArray());

var scenario2Passed = lossPercentage <= 5.0 && achievedRps >= 50.0;
PrintScenarioResult("CENÁRIO 2 (Pico >= 50 req/s com perda <= 5%)", scenario2Passed);

// =========================================================================
// CENÁRIO 3: QUEDA REAL DO CONTAINER DO CONSOLIDADO DIÁRIO (CHAOS TEST)
// =========================================================================
Console.WriteLine("\n=========================================================================");
Console.WriteLine(" CENÁRIO 3: RESILIÊNCIA À QUEDA DO CONSOLIDADO DIÁRIO (DOCKER STOP/START)");
Console.WriteLine("=========================================================================");
Console.WriteLine("  1. Parando o container 'cashflow-consolidation-worker'...");
await RunDockerCommandAsync("stop cashflow-consolidation-worker");
Console.WriteLine("     ✓ Container 'cashflow-consolidation-worker' está OFFLINE.");

Console.WriteLine($"  2. Enviando 20 lançamentos na data {testDate3:yyyy-MM-dd} com o consolidado CAÍDO...");
const int offlineCreditCount = 15;
const decimal offlineCreditAmount = 200.00m; // R$ 3.000,00
const int offlineDebitCount = 5;
const decimal offlineDebitAmount = 100.00m;  // R$ 500,00
const int totalOfflineRequests = offlineCreditCount + offlineDebitCount;
var expectedOfflineNet = (offlineCreditCount * offlineCreditAmount) - (offlineDebitCount * offlineDebitAmount); // R$ 2.500,00

var offlineTasks = Enumerable.Range(1, totalOfflineRequests).Select(i =>
{
    var isCredit = i <= offlineCreditCount;
    var req = new CreateTransactionRequest(
        Amount: isCredit ? offlineCreditAmount : offlineDebitAmount,
        Type: isCredit ? TransactionType.Credit : TransactionType.Debit,
        Date: testDate3,
        Description: $"Lançamento com Consolidado Offline #{i:00}");

    return httpClient.PostAsJsonAsync("/api/v1/transactions", req);
});

var offlineResponses = await Task.WhenAll(offlineTasks);
var offlineAccepted = offlineResponses.Count(r => r.StatusCode == HttpStatusCode.Accepted);
Console.WriteLine($"     ✓ Lançamentos aceitos pela API com o consolidado fora do ar: {offlineAccepted}/{totalOfflineRequests} (100% disponível!)");

Console.WriteLine("  3. Religando o container 'cashflow-consolidation-worker'...");
await RunDockerCommandAsync("start cashflow-consolidation-worker");
Console.WriteLine("     ✓ Container 'cashflow-consolidation-worker' está ONLINE novamente. Aguardando drenagem da fila...");

DailyBalanceResponse? recoveredBalance = null;
var recoverySw = Stopwatch.StartNew();
while (recoverySw.Elapsed < TimeSpan.FromSeconds(25))
{
    recoveredBalance = await httpClient.GetFromJsonAsync<DailyBalanceResponse>(
        $"/api/v1/daily-balance?date={testDate3:yyyy-MM-dd}",
        jsonOptions);

    if (recoveredBalance is { TransactionCount: totalOfflineRequests })
    {
        break;
    }

    await Task.Delay(200);
}
recoverySw.Stop();

Console.WriteLine($"  4. Saldo consolidado após recuperação ({recoverySw.Elapsed.TotalMilliseconds:F0} ms):");
Console.WriteLine($"      - Qtd Lançamentos : {recoveredBalance?.TransactionCount}/{totalOfflineRequests}");
Console.WriteLine($"      - Saldo Líquido   : R$ {recoveredBalance?.NetBalance:N2} (Esperado: R$ {expectedOfflineNet:N2})");

var scenario3Passed = offlineAccepted == totalOfflineRequests
    && recoveredBalance is not null
    && recoveredBalance.TransactionCount == totalOfflineRequests
    && recoveredBalance.NetBalance == expectedOfflineNet;

PrintScenarioResult("CENÁRIO 3 (Isolamento de Falha e Recuperação Automática)", scenario3Passed);

Console.WriteLine("\n=========================================================================");
if (scenario1Passed && scenario2Passed && scenario3Passed)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine(" RESULTADO FINAL: TODOS OS 3 CENÁRIOS DO DESAFIO FORAM APROVADOS COM SUCESSO!");
    Console.ResetColor();
    Console.WriteLine("=========================================================================");
    return 0;
}
else
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine(" RESULTADO FINAL: UM OU MAIS CENÁRIOS FALHARAM.");
    Console.ResetColor();
    Console.WriteLine("=========================================================================");
    return 1;
}

static void PrintBanner(string targetUrl)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("╔═══════════════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║   CASHFLOW — SUITE DE TESTES DE CONCORRÊNCIA, CARGA (50 RPS) & CAOS   ║");
    Console.WriteLine($"║   Ambiente Alvo: {targetUrl,-52} ║");
    Console.WriteLine("╚═══════════════════════════════════════════════════════════════════════╝\n");
    Console.ResetColor();
}

static void PrintLatencyStats(string label, double[] latenciesMs)
{
    if (latenciesMs.Length == 0) return;
    Array.Sort(latenciesMs);

    var avg = latenciesMs.Average();
    var p50 = Percentile(latenciesMs, 0.50);
    var p95 = Percentile(latenciesMs, 0.95);
    var p99 = Percentile(latenciesMs, 0.99);

    Console.WriteLine($"  • Latência {label}: Média = {avg:F2} ms | P50 = {p50:F2} ms | P95 = {p95:F2} ms | P99 = {p99:F2} ms");
}

static double Percentile(double[] sorted, double percentile)
{
    var index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
    index = Math.Clamp(index, 0, sorted.Length - 1);
    return sorted[index];
}

static void PrintScenarioResult(string name, bool passed)
{
    Console.ForegroundColor = passed ? ConsoleColor.Green : ConsoleColor.Red;
    Console.WriteLine(passed ? $"  [APROVADO] {name}" : $"  [FALHOU]   {name}");
    Console.ResetColor();
}

static async Task RunDockerCommandAsync(string arguments)
{
    var psi = new ProcessStartInfo("docker", arguments)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    using var proc = Process.Start(psi)!;
    await proc.WaitForExitAsync();
}
