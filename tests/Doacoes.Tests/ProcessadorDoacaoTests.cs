using Doacoes.Worker.Processamento;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Doacoes.Tests;

// Teste de integração com PostgreSQL de verdade (Testcontainers; precisa de Docker, que o runner do GitHub tem).
// O esquema abaixo reproduz só as colunas que o worker usa das tabelas criadas pela campanhas-api.
public class ProcessadorDoacaoTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private NpgsqlDataSource _dataSource = null!;
    private ProcessadorDoacao _processador = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _dataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        _processador = new ProcessadorDoacao(_dataSource, new FakeTimeProvider(Agora));

        await Executar("""
            CREATE TABLE campanhas (
                id uuid PRIMARY KEY, status varchar(20) NOT NULL, data_fim timestamptz NOT NULL,
                valor_arrecadado numeric(14,2) NOT NULL DEFAULT 0);
            CREATE TABLE doacoes (
                id uuid PRIMARY KEY, campanha_id uuid NOT NULL REFERENCES campanhas(id), valor numeric(14,2) NOT NULL,
                status varchar(20) NOT NULL, motivo_rejeicao varchar(300), criada_em timestamptz NOT NULL, processada_em timestamptz);
            """);
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Soma_o_valor_e_marca_Processada()
    {
        var campanha = await NovaCampanha("Ativa", Agora.AddDays(10));
        var doacao = await NovaDoacao(campanha, 75.50m);

        var resultado = await _processador.ProcessarAsync(doacao);

        Assert.Equal(ResultadoProcessamento.Processada, resultado);
        Assert.Equal(75.50m, await ValorArrecadado(campanha));
        Assert.Equal("Processada", await StatusDoacao(doacao));
    }

    [Fact]
    public async Task Mensagem_repetida_nao_soma_duas_vezes()
    {
        var campanha = await NovaCampanha("Ativa", Agora.AddDays(10));
        var doacao = await NovaDoacao(campanha, 100m);

        await _processador.ProcessarAsync(doacao);
        var segunda = await _processador.ProcessarAsync(doacao);

        Assert.Equal(ResultadoProcessamento.JaProcessada, segunda);
        Assert.Equal(100m, await ValorArrecadado(campanha));
    }

    [Fact]
    public async Task Mensagens_simultaneas_nao_perdem_soma()
    {
        var campanha = await NovaCampanha("Ativa", Agora.AddDays(10));
        var doacoes = new List<Guid>();
        for (var i = 0; i < 20; i++)
            doacoes.Add(await NovaDoacao(campanha, 10m));

        // Cada doação chega duas vezes, tudo em paralelo.
        await Task.WhenAll(doacoes.Concat(doacoes).Select(d => _processador.ProcessarAsync(d)));

        Assert.Equal(200m, await ValorArrecadado(campanha));
    }

    [Theory]
    [InlineData("Cancelada")]
    [InlineData("Concluida")]
    public async Task Campanha_que_deixou_de_aceitar_marca_Rejeitada_sem_somar(string status)
    {
        var campanha = await NovaCampanha(status, Agora.AddDays(10));
        var doacao = await NovaDoacao(campanha, 30m);

        var resultado = await _processador.ProcessarAsync(doacao);

        Assert.Equal(ResultadoProcessamento.Rejeitada, resultado);
        Assert.Equal(0m, await ValorArrecadado(campanha));
        Assert.Equal("Rejeitada", await StatusDoacao(doacao));
    }

    [Fact]
    public async Task Doacao_aceita_no_prazo_e_processada_depois_do_termino_ainda_soma()
    {
        var campanha = await NovaCampanha("Ativa", Agora.AddHours(-1));
        var doacao = await NovaDoacao(campanha, 20m, criadaEm: Agora.AddHours(-2));

        Assert.Equal(ResultadoProcessamento.Processada, await _processador.ProcessarAsync(doacao));
        Assert.Equal(20m, await ValorArrecadado(campanha));
    }

    [Fact]
    public async Task Doacao_inexistente_nao_quebra()
        => Assert.Equal(ResultadoProcessamento.NaoEncontrada, await _processador.ProcessarAsync(Guid.NewGuid()));

    private async Task<Guid> NovaCampanha(string status, DateTimeOffset dataFim)
    {
        var id = Guid.NewGuid();
        await Executar("INSERT INTO campanhas (id, status, data_fim) VALUES (@id, @status, @fim)",
            ("id", id), ("status", status), ("fim", dataFim.UtcDateTime));
        return id;
    }

    private async Task<Guid> NovaDoacao(Guid campanha, decimal valor, DateTimeOffset? criadaEm = null)
    {
        var id = Guid.NewGuid();
        await Executar("INSERT INTO doacoes (id, campanha_id, valor, status, criada_em) VALUES (@id, @c, @v, 'Pendente', @em)",
            ("id", id), ("c", campanha), ("v", valor), ("em", (criadaEm ?? Agora).UtcDateTime));
        return id;
    }

    private async Task<decimal> ValorArrecadado(Guid campanha)
    {
        await using var cmd = _dataSource.CreateCommand("SELECT valor_arrecadado FROM campanhas WHERE id = @id");
        cmd.Parameters.AddWithValue("id", campanha);
        return (decimal)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<string> StatusDoacao(Guid doacao)
    {
        await using var cmd = _dataSource.CreateCommand("SELECT status FROM doacoes WHERE id = @id");
        cmd.Parameters.AddWithValue("id", doacao);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task Executar(string sql, params (string Nome, object Valor)[] parametros)
    {
        await using var cmd = _dataSource.CreateCommand(sql);
        foreach (var (nome, valor) in parametros)
            cmd.Parameters.AddWithValue(nome, valor);
        await cmd.ExecuteNonQueryAsync();
    }
}
