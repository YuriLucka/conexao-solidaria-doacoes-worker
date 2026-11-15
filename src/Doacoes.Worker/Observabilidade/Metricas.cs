using Prometheus;

namespace Doacoes.Worker.Observabilidade;

// Métricas de negócio expostas em /metrics (Prometheus -> Grafana).
public static class Metricas
{
    public static readonly Counter DoacoesProcessadas = Prometheus.Metrics.CreateCounter(
        "doacoes_processadas_total",
        "Mensagens DoacaoRecebidaEvent tratadas pelo worker, por resultado.",
        new CounterConfiguration { LabelNames = ["resultado"] });

    public static readonly Counter ValorArrecadado = Prometheus.Metrics.CreateCounter(
        "doacoes_valor_processado_reais_total",
        "Soma, em reais, das doações processadas pelo worker.");

    public static readonly Histogram DuracaoProcessamento = Prometheus.Metrics.CreateHistogram(
        "doacoes_processamento_segundos",
        "Tempo para processar uma doação (transação no PostgreSQL).",
        new HistogramConfiguration { Buckets = Histogram.ExponentialBuckets(0.005, 2, 10) });
}
