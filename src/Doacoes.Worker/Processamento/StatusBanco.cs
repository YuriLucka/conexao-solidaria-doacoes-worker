namespace Doacoes.Worker.Processamento;

// Valores das colunas status gravados pela campanhas-api (enums do domínio salvos como texto).
public static class StatusBanco
{
    public const string DoacaoPendente = "Pendente";
    public const string DoacaoProcessada = "Processada";
    public const string DoacaoRejeitada = "Rejeitada";

    public const string CampanhaAtiva = "Ativa";
    public const string CampanhaConcluida = "Concluida";
    public const string CampanhaCancelada = "Cancelada";
}
