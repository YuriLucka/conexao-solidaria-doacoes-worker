namespace Doacoes.Worker.Processamento;

public interface IProcessadorDoacao
{
    Task<ResultadoProcessamento> ProcessarAsync(Guid doacaoId, CancellationToken ct = default);
}
