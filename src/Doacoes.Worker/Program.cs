using System.Text.Json;
using ConexaoSolidaria.Messaging;
using Doacoes.Worker.Consumers;
using Doacoes.Worker.Observabilidade;
using Doacoes.Worker.Processamento;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Prometheus;
using StackExchange.Redis;

// Worker de doações. É um host web só para expor /health e /metrics; o trabalho é o consumidor do RabbitMQ.
var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});

var postgres = builder.Configuration.GetConnectionString("Postgres");
if (string.IsNullOrWhiteSpace(postgres))
    throw new InvalidOperationException("ConnectionStrings:Postgres não configurado.");
builder.Services.AddNpgsqlDataSource(postgres);

var redis = builder.Configuration.GetConnectionString("Redis");
if (string.IsNullOrWhiteSpace(redis))
    throw new InvalidOperationException("ConnectionStrings:Redis não configurado.");
builder.Services.AddStackExchangeRedisCache(o =>
{
    var opcoes = ConfigurationOptions.Parse(redis);
    opcoes.AbortOnConnectFail = false;
    opcoes.ConnectTimeout = 2000;
    opcoes.SyncTimeout = 2000;
    o.ConfigurationOptions = opcoes;
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IProcessadorDoacao, ProcessadorDoacao>();

// Fila "doacao-recebida"; até 10 mensagens em paralelo.
builder.Services.AddConexaoMessaging(builder.Configuration, x =>
    x.AddConsumer<DoacaoRecebidaConsumer>((_, c) => c.ConcurrentMessageLimit = 10));

builder.Services.AddHealthChecks().AddCheck<PostgresHealthCheck>("postgres");

var app = builder.Build();

app.UseHttpMetrics();

// /health: inclui PostgreSQL e a conexão com o RabbitMQ (check do MassTransit). /health/live: processo de pé.
app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = EscreverHealthAsync });
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = EscreverHealthAsync });
app.MapMetrics();

app.Run();

static Task EscreverHealthAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    return context.Response.WriteAsync(JsonSerializer.Serialize(new
    {
        status = report.Status.ToString(),
        servico = "doacoes-worker",
        checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString())
    }));
}

public partial class Program { } // usado pelos testes
