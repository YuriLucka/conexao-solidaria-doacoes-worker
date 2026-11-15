using ConexaoSolidaria.Contracts.Events;
using Doacoes.Worker.Observabilidade;
using Doacoes.Worker.Processamento;
using MassTransit;
using Microsoft.Extensions.Caching.Distributed;
using Prometheus;

namespace Doacoes.Worker.Consumers;

// Consome a fila "doacao-recebida". Exceção (banco fora, por exemplo) volta para o retry do MassTransit
// e, esgotadas as tentativas, a mensagem vai para "doacao-recebida_error".
public class DoacaoRecebidaConsumer : IConsumer<DoacaoRecebidaEvent>
{
    // Mesma chave usada pela campanhas-api no cache do painel.
    public const string ChaveCachePainel = "campanhas:ativas";

    private readonly IProcessadorDoacao _processador;
    private readonly IDistributedCache _cache;
    private readonly ILogger<DoacaoRecebidaConsumer> _logger;

    public DoacaoRecebidaConsumer(IProcessadorDoacao processador, IDistributedCache cache, ILogger<DoacaoRecebidaConsumer> logger)
    {
        _processador = processador;
        _cache = cache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<DoacaoRecebidaEvent> context)
    {
        var evento = context.Message;

        ResultadoProcessamento resultado;
        using (Metricas.DuracaoProcessamento.NewTimer())
            resultado = await _processador.ProcessarAsync(evento.DoacaoId, context.CancellationToken);

        Metricas.DoacoesProcessadas.WithLabels(resultado.ToString()).Inc();

        switch (resultado)
        {
            case ResultadoProcessamento.Processada:
                Metricas.ValorArrecadado.Inc((double)evento.ValorDoacao);
                _logger.LogInformation("DOACAO Processada {DoacaoId}: R$ {Valor} somados na campanha {CampanhaId}",
                    evento.DoacaoId, evento.ValorDoacao, evento.IdCampanha);
                await InvalidarPainelAsync();
                break;
            case ResultadoProcessamento.Rejeitada:
                _logger.LogWarning("DOACAO Rejeitada {DoacaoId}: campanha {CampanhaId} não aceita mais doações",
                    evento.DoacaoId, evento.IdCampanha);
                break;
            case ResultadoProcessamento.JaProcessada:
                _logger.LogInformation("DOACAO {DoacaoId} já processada antes; mensagem repetida ignorada", evento.DoacaoId);
                break;
            case ResultadoProcessamento.NaoEncontrada:
                _logger.LogWarning("DOACAO {DoacaoId} não existe no banco; mensagem ignorada", evento.DoacaoId);
                break;
        }
    }

    // O painel público passa a mostrar o valor novo na próxima leitura. Falha do Redis não desfaz o processamento:
    // o cache expira sozinho em 60 s.
    private async Task InvalidarPainelAsync()
    {
        try
        {
            await _cache.RemoveAsync(ChaveCachePainel);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis indisponível ao invalidar {Chave}", ChaveCachePainel);
        }
    }
}
