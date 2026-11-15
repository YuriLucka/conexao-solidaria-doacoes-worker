using ConexaoSolidaria.Contracts.Events;
using Doacoes.Worker.Consumers;
using Doacoes.Worker.Processamento;
using MassTransit;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Doacoes.Tests;

public class DoacaoRecebidaConsumerTests
{
    private readonly Mock<IProcessadorDoacao> _processador = new();
    private readonly Mock<IDistributedCache> _cache = new();
    private readonly DoacaoRecebidaConsumer _consumer;
    private readonly DoacaoRecebidaEvent _evento = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 50m, DateTime.UtcNow);

    public DoacaoRecebidaConsumerTests()
        => _consumer = new DoacaoRecebidaConsumer(_processador.Object, _cache.Object, NullLogger<DoacaoRecebidaConsumer>.Instance);

    private Task Consumir()
    {
        var contexto = new Mock<ConsumeContext<DoacaoRecebidaEvent>>();
        contexto.SetupGet(c => c.Message).Returns(_evento);
        contexto.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return _consumer.Consume(contexto.Object);
    }

    [Fact]
    public async Task Doacao_processada_invalida_o_cache_do_painel()
    {
        _processador.Setup(p => p.ProcessarAsync(_evento.DoacaoId, It.IsAny<CancellationToken>())).ReturnsAsync(ResultadoProcessamento.Processada);

        await Consumir();

        _cache.Verify(c => c.RemoveAsync(DoacaoRecebidaConsumer.ChaveCachePainel, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(ResultadoProcessamento.Rejeitada)]
    [InlineData(ResultadoProcessamento.JaProcessada)]
    [InlineData(ResultadoProcessamento.NaoEncontrada)]
    public async Task Sem_soma_nao_mexe_no_cache(ResultadoProcessamento resultado)
    {
        _processador.Setup(p => p.ProcessarAsync(_evento.DoacaoId, It.IsAny<CancellationToken>())).ReturnsAsync(resultado);

        await Consumir();

        _cache.Verify(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Redis_fora_nao_derruba_o_processamento()
    {
        _processador.Setup(p => p.ProcessarAsync(_evento.DoacaoId, It.IsAny<CancellationToken>())).ReturnsAsync(ResultadoProcessamento.Processada);
        _cache.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());

        await Consumir();
    }

    [Fact]
    public async Task Falha_no_banco_propaga_para_o_retry_do_MassTransit()
    {
        _processador.Setup(p => p.ProcessarAsync(_evento.DoacaoId, It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("banco fora"));

        await Assert.ThrowsAsync<TimeoutException>(Consumir);
    }
}
