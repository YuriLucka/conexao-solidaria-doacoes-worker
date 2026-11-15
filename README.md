# Conexão Solidária - Doações Worker

Microsserviço consumidor da plataforma Conexão Solidária (Hackathon FIAP Pós Tech 11NETT, Fase 5).
Instruções para subir a solução inteira: repositório [conexao-solidaria-orchestration](https://github.com/YuriLucka/conexao-solidaria-orchestration).

## O que faz

Consome `DoacaoRecebidaEvent` da fila `doacao-recebida` (RabbitMQ, MassTransit) e atualiza o valor total arrecadado da campanha. É o único serviço que escreve `campanhas.valor_arrecadado`.

Em uma transação no PostgreSQL, com as linhas travadas (`FOR UPDATE`):

1. lê a doação; se não está mais `Pendente`, a mensagem é repetida e nada muda (idempotência);
2. se a campanha ainda está `Ativa` e a doação foi feita dentro do prazo, soma o valor gravado na doação e marca `Processada`;
3. se a campanha foi concluída ou cancelada antes do processamento, marca `Rejeitada` sem somar.

Depois apaga a chave `campanhas:ativas` do Redis, para o painel público mostrar o valor novo. Falha técnica (banco fora) volta para o retry do MassTransit (3 tentativas) e depois para a fila `doacao-recebida_error`.

Expõe `/health` (PostgreSQL e RabbitMQ), `/health/live` e `/metrics` com `doacoes_processadas_total{resultado}`, `doacoes_valor_processado_reais_total` e `doacoes_processamento_segundos`.

## Rodar só este serviço

Pré-requisitos: .NET SDK 10, infraestrutura do Compose e a campanhas-api já ter rodado uma vez (as tabelas são criadas pelas migrações dela).

```bash
dotnet run --project src/Doacoes.Worker --launch-profile http   # http://localhost:5090/health
dotnet test Doacoes.slnx   # inclui integração com PostgreSQL via Testcontainers (precisa de Docker)
```

## CI

`.github/workflows/ci.yml`, a cada push na `main`: restore, build, testes e imagem Docker publicada em `ghcr.io/yurilucka/conexao-solidaria-doacoes-worker` (tags `sha-<commit>` e `latest`).
