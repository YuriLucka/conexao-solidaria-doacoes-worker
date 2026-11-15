namespace Doacoes.Worker.Processamento;

public enum ResultadoProcessamento
{
    // Valor somado na campanha; doação marcada Processada.
    Processada,
    // Campanha deixou de aceitar doações antes do processamento; doação marcada Rejeitada.
    Rejeitada,
    // Mensagem repetida: a doação já tinha saído de Pendente. Nada muda (idempotência).
    JaProcessada,
    // Doação inexistente no banco. Nada muda.
    NaoEncontrada
}

public interface IProcessadorDoacao
{
    Task<ResultadoProcessamento> ProcessarAsync(Guid doacaoId, CancellationToken ct = default);
}
