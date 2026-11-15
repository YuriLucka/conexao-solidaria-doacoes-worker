using Npgsql;

namespace Doacoes.Worker.Processamento;

// Único ponto do sistema que altera campanhas.valor_arrecadado.
// Tudo numa transação, com as linhas da doação e da campanha travadas (FOR UPDATE):
// duas mensagens iguais ao mesmo tempo não somam duas vezes, e duas doações para a mesma campanha não se perdem.
// O valor somado vem da linha da doação gravada pela API, não da mensagem.
// As tabelas são criadas pelas migrações da campanhas-api; o worker não tem migrações.
public class ProcessadorDoacao : IProcessadorDoacao
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly TimeProvider _relogio;

    public ProcessadorDoacao(NpgsqlDataSource dataSource, TimeProvider relogio)
    {
        _dataSource = dataSource;
        _relogio = relogio;
    }

    public async Task<ResultadoProcessamento> ProcessarAsync(Guid doacaoId, CancellationToken ct = default)
    {
        await using var conexao = await _dataSource.OpenConnectionAsync(ct);
        await using var transacao = await conexao.BeginTransactionAsync(ct);

        Guid campanhaId;
        decimal valor;
        DateTime criadaEm;
        await using (var cmd = new NpgsqlCommand(
            "SELECT campanha_id, valor, status, criada_em FROM doacoes WHERE id = @id FOR UPDATE", conexao, transacao))
        {
            cmd.Parameters.AddWithValue("id", doacaoId);
            await using var leitor = await cmd.ExecuteReaderAsync(ct);
            if (!await leitor.ReadAsync(ct))
                return ResultadoProcessamento.NaoEncontrada;
            if (leitor.GetString(2) != "Pendente")
                return ResultadoProcessamento.JaProcessada;

            campanhaId = leitor.GetGuid(0);
            valor = leitor.GetDecimal(1);
            criadaEm = leitor.GetDateTime(3);
        }

        string statusCampanha;
        DateTime dataFim;
        await using (var cmd = new NpgsqlCommand(
            "SELECT status, data_fim FROM campanhas WHERE id = @id FOR UPDATE", conexao, transacao))
        {
            cmd.Parameters.AddWithValue("id", campanhaId);
            await using var leitor = await cmd.ExecuteReaderAsync(ct);
            await leitor.ReadAsync(ct);
            statusCampanha = leitor.GetString(0);
            dataFim = leitor.GetDateTime(1);
        }

        var agora = _relogio.GetUtcNow().UtcDateTime;

        // A doação foi aceita pela API dentro do prazo; o atraso do worker não a invalida.
        // Só é rejeitada se a campanha foi concluída ou cancelada antes do processamento.
        var aceita = statusCampanha == "Ativa" && criadaEm <= dataFim;
        if (aceita)
        {
            await using (var cmd = new NpgsqlCommand(
                "UPDATE campanhas SET valor_arrecadado = valor_arrecadado + @valor WHERE id = @id", conexao, transacao))
            {
                cmd.Parameters.AddWithValue("valor", valor);
                cmd.Parameters.AddWithValue("id", campanhaId);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await AtualizarDoacaoAsync(conexao, transacao, doacaoId, "Processada", null, agora, ct);
        }
        else
        {
            var motivo = $"Campanha com status {statusCampanha} no momento do processamento.";
            await AtualizarDoacaoAsync(conexao, transacao, doacaoId, "Rejeitada", motivo, agora, ct);
        }

        await transacao.CommitAsync(ct);
        return aceita ? ResultadoProcessamento.Processada : ResultadoProcessamento.Rejeitada;
    }

    private static async Task AtualizarDoacaoAsync(NpgsqlConnection conexao, NpgsqlTransaction transacao,
        Guid doacaoId, string status, string? motivo, DateTime agora, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "UPDATE doacoes SET status = @status, motivo_rejeicao = @motivo, processada_em = @agora WHERE id = @id",
            conexao, transacao);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("motivo", (object?)motivo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("agora", agora);
        cmd.Parameters.AddWithValue("id", doacaoId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
