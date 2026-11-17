# ADR-004: Estratégia de Cache Distribuído (Redis) vs. Cache em Memória e Fonte da Verdade no PostgreSQL

- **Status:** Aceito
- **Data:** 2026-11-16

---

## Contexto

O desafio solicita considerar **dimensionamento horizontal, balanceamento de carga e estratégias de cache**, além de suportar picos de **50 requisições por segundo** na consulta do consolidado diário.

Precisávamos responder a duas questões arquiteturais:
1. Por que não utilizar Cache em Memória local (`IMemoryCache`) na Web API?
2. O Redis deve ser usado como banco único do saldo consolidado ou apenas como camada de cache?

## Decisão

1. **Descarte do Cache em Memória Local (`IMemoryCache`):**
   - Quando a `CashFlow.Api` é escalada horizontalmente (2 ou mais réplicas atrás de um balanceador de carga), cada instância possui sua própria memória RAM isolada.
   - Além de o `ConsolidationWorker` (que roda em outro processo/container) não conseguir atualizar diretamente a memória RAM local das instâncias da Web API, duas requisições consecutivas do comerciante poderiam cair em instâncias diferentes e retornar **saldos divergentes**.

2. **PostgreSQL como Fonte da Verdade + Redis como Cache Distribuído:**
   - **PostgreSQL (`daily_balances`):** É a **Fonte da Verdade (Source of Truth)** financeira. Como existe apenas 1 registro consolidado por dia, a tabela cresce apenas **365 linhas por ano**, permitindo buscas por chave primária (`date`) em ~1ms e garantindo durabilidade ACID.
   - **Redis (`IDistributedCache`):** Atua exclusivamente como **Cache Distribuído** compartilhado por todas as instâncias:
     - **Atualização Pró-ativa no Evento (Write-Through no Worker 2):** Assim que o `ConsolidationWorker` confirma a transação no PostgreSQL, ele já atualiza a chave `cashflow:daily-balance:{yyyy-MM-dd}` no Redis.
     - **Leitura com Fallback Resiliente (Cache-Aside na Web API):** A `CashFlow.Api` consulta primeiro o Redis. Em caso de *Cache Hit*, responde imediatamente sem tocar no PostgreSQL. Em caso de *Cache Miss* **ou se o Redis cair**, a API faz *fallback* transparente para a tabela `daily_balances` no PostgreSQL (degradação graciosa sem erro para o usuário).

## Consequências

- **Positivas:**
  - Consistência de cache entre múltiplas instâncias da `CashFlow.Api` em escala horizontal.
  - Se o Redis cair, o sistema continua funcionando perfeitamente lendo o saldo pré-computado no PostgreSQL.
  - Se o PostgreSQL sofrer lentidão momentânea, as leituras de saldo já cacheadas no Redis continuam respondendo em sub-milissegundo.
