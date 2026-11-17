# ADR-003: Consistência Eventual, Idempotência e Garantia de Entrega

- **Status:** Aceito
- **Data:** 2026-11-16

---

## Contexto

Ao desacoplar a entrada de lançamentos (`CashFlow.Api`), a persistência (`TransactionWorker`) e o cálculo do saldo diário (`ConsolidationWorker`) por meio de filas no RabbitMQ, surgem três desafios clássicos de sistemas distribuídos:
1. **Consistência Eventual:** Um cliente pode realizar um lançamento e consultar o saldo poucos milissegundos depois, antes de o `ConsolidationWorker` concluir o processamento do evento.
2. **Falhas de Mensageria ou Banco de Dados:** O banco de dados pode oscilar enquanto mensagens estão em trânsito.
3. **Entrega *At-Least-Once* (Mensagens Duplicadas):** Em caso de *timeout* de rede ou reinício de um Worker antes do envio do `ACK` ao RabbitMQ, a mesma mensagem será reentregue. Em um sistema financeiro, processar o mesmo lançamento duas vezes corrompe o saldo diário.

## Decisão

1. **Semântica HTTP Explícita para Consistência Eventual (`202 Accepted`):**
   - O endpoint `POST /api/v1/transactions` retorna o status HTTP **`202 Accepted`** contendo o `TransactionId` gerado na entrada e o header `Location` apontando para `/api/v1/daily-balance/{date}`.
   - O endpoint `GET /api/v1/daily-balance/{date}` retorna os metadados `transactionCount` (quantidade de lançamentos consolidados no dia) e `lastUpdatedAtUtc` (data/hora da última consolidação), dando total visibilidade ao consumidor.

2. **Filas Duráveis e Retry Exponencial:**
   - Ambas as filas (`transactions-queue` e `consolidation-queue`) são configuradas com `Durable = true` e mensagens persistentes em disco.
   - Os consumidores utilizam política de **Retry Exponencial** (5 tentativas com backoff exponencial) antes de mover mensagens problemáticas para a fila de erro (*Dead-Letter / Error Queue* do MassTransit).
   - O `ACK` só é enviado ao RabbitMQ após a conclusão integral de cada etapa.

3. **Idempotência Ponta a Ponta (Zero Duplicidade):**
   - **No `TransactionWorker` (Worker 1):** O `TransactionId` (`Guid`) é gerado na `CashFlow.Api`. O repositório verifica a existência da chave primária (`Id`) e trata conflitos de chave única (`DbUpdateException`). Se a mensagem for reentregue pela Fila 1, o lançamento não é duplicado na tabela `transactions` e o evento segue normalmente para a Fila 2.
   - **No `ConsolidationWorker` (Worker 2):** Utilizamos uma tabela de controle de idempotência (`processed_transactions`) atualizada **dentro da mesma transação atômica de banco de dados** que altera a tabela `daily_balances`, aliada ao controle de concorrência otimista (`xmin` no PostgreSQL). Se o mesmo `TransactionId` chegar mais de uma vez pela Fila 2, o Worker identifica que já foi processado e ignora a soma duplicada.

## Consequências

- **Positivas:**
  - Garantia matemática de que nenhum lançamento é perdido ou somado em duplicidade.
  - Em condições normais, a janela de consistência eventual é inferior a **20–50ms**.
- **Negativas / Trade-offs:**
  - Necessidade de manter a tabela `processed_transactions` para auditoria e idempotência (custo de armazenamento residual e facilmente particionável por data).
