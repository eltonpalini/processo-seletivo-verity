# ADR-001: Arquitetura Orientada a Eventos com Filas e Workers Isolados (SEDA / Bulkhead)

- **Status:** Aceito
- **Data:** 2026-11-16

---

## Contexto

O sistema de Fluxo de Caixa Diário precisa atender a dois requisitos de negócio principais:
1. Controle de lançamentos financeiros (débitos e créditos).
2. Relatório de saldo diário consolidado.

Além disso, os requisitos não-funcionais estabelecem que:
- **O serviço de controle de lançamentos não pode ficar indisponível caso o sistema de consolidado diário caia.**
- **Nenhum lançamento pode ser perdido**, visto que a perda de um lançamento corrompe diretamente o saldo contábil do comerciante.
- **Em momentos de pico, a consulta do consolidado diário recebe 50 requisições por segundo, com no máximo 5% de perda.**

## Decisão

Adotamos uma arquitetura inspirada em **SEDA (Staged Event-Driven Architecture)** combinada com **Queue-Based Load Leveling** e **Bulkhead Pattern** (isolamento de processamento), dividindo a execução em três processos independentes comunicando-se por duas filas duráveis no **RabbitMQ**:

1. **`CashFlow.Api` (Web API):**
   - No `POST /api/v1/transactions`: realiza apenas a validação de contrato/domínio, gera o identificador único (`TransactionId`) e publica a mensagem na **Fila 1 (`transactions-queue`)**, retornando imediatamente `202 Accepted`.
   - No `GET /api/v1/daily-balance/{date}`: entrega o saldo diário **já pré-computado** (sem realizar `SUM()` em tempo real), consultando o cache distribuído (**Redis**) com *fallback* para o **PostgreSQL**.
2. **`CashFlow.TransactionWorker` (Worker 1 — Gravação de Lançamento):**
   - Consome a **Fila 1 (`transactions-queue`)**, persiste o lançamento no **PostgreSQL** de forma idempotente e, após a confirmação da escrita, publica um evento na **Fila 2 (`consolidation-queue`)**.
3. **`CashFlow.ConsolidationWorker` (Worker 2 — Consolidação de Saldo):**
   - Consome a **Fila 2 (`consolidation-queue`)**, atualiza de forma atômica e idempotente o registro consolidado do dia na tabela `daily_balances` (**PostgreSQL**) e sincroniza o cache no **Redis**.

## Justificativa (Por que dois Workers separados?)

1. **Isolamento de Falhas (Atendimento ao Requisito Obrigatório):**
   Se o `ConsolidationWorker` cair ou ficar indisponível, a `CashFlow.Api` continua recebendo lançamentos e o `TransactionWorker` continua gravando-os no banco de dados normalmente. Os eventos de consolidação ficam retidos com segurança na `consolidation-queue` e são processados assim que o Worker 2 se recupera.
2. **Ausência de Concorrência entre Escrita Bruta e Cálculo de Consolidação:**
   Ao isolar a gravação do lançamento (Worker 1) do cálculo de saldo (Worker 2), um pico de lançamentos não disputa threads ou locks da tabela de saldo consolidado, e vice-versa.
3. **Leitura Pré-Computada ($O(1)$):**
   A consolidação não é um endpoint chamado sob demanda para calcular o saldo na hora; ela é um **gatilho orientado a evento** disparado a cada lançamento gravado. Assim, quando o endpoint `GET /api/v1/daily-balance/{date}` é requisitado (mesmo em picos muito superiores a 50 req/s), o retorno é imediato, pois entrega um dado já computado.

## Consequências

- **Positivas:**
  - Altíssima resiliência na ingestão de lançamentos (mesmo sob oscilação momentânea do banco de dados, a Fila 1 absorve a carga).
  - Isolamento completo entre o fluxo de lançamento e o fluxo de consolidação.
  - Tempo de resposta sub-5ms tanto no `POST` quanto no `GET`.
- **Negativas / Trade-offs:**
  - Introdução de consistência eventual entre o instante do `POST` e a atualização do saldo consolidado (tratada no [ADR-003](0003-eventual-consistency-and-idempotency.md)).
  - **A `CashFlow.Api` depende da disponibilidade do RabbitMQ no momento do `POST`.** O isolamento garantido por esta decisão é entre o fluxo de lançamento e o *consolidado diário* (que pode cair sem afetar a ingestão). Ele não cobre a indisponibilidade do próprio broker: se o RabbitMQ estiver fora do ar, a publicação na Fila 1 falha e o `POST` retorna erro (o lançamento não é aceito, portanto nenhum lançamento é perdido silenciosamente, mas a ingestão fica indisponível). Mitigações previstas:
    - Em produção, o RabbitMQ deve operar em cluster com *quorum queues* (3 nós), eliminando o broker como ponto único de falha.
    - Evolução futura: *Outbox Pattern* na borda da API (gravar o comando em uma tabela local e publicar de forma assíncrona), desacoplando a aceitação do lançamento da disponibilidade do broker. Detalhado em [Evoluções Futuras](../architecture.md#5-evoluções-futuras-para-produção).
  - **Leitura e escrita compartilham a mesma API e o mesmo banco.** O `GET` do consolidado roda no mesmo processo do `POST` e o saldo é lido da mesma instância PostgreSQL em que os workers escrevem (com o Redis absorvendo a maior parte das leituras). Um pico de leitura que ultrapasse o cache pode, portanto, competir por recursos com a ingestão. A separação completa (API de consulta dedicada e réplica de leitura do banco) está descrita em [Evoluções Futuras](../architecture.md#5-evoluções-futuras-para-produção).
