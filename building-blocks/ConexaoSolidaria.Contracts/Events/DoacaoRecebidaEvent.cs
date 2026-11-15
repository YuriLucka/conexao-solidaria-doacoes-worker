namespace ConexaoSolidaria.Contracts.Events;

// Publicado pela campanhas-api ao registrar uma doação; consumido pelo doacoes-worker,
// que é o único serviço que atualiza o valor arrecadado da campanha.
// O mesmo arquivo existe nos dois repositórios: o MassTransit roteia pelo nome completo do tipo.
public record DoacaoRecebidaEvent(
    Guid DoacaoId,
    Guid IdCampanha,
    Guid DoadorId,
    decimal ValorDoacao,
    DateTime OcorridaEm);
