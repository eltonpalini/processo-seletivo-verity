# ADR-002: Decisão de Não Utilizar um API Gateway na Aplicação

- **Status:** Aceito
- **Data:** 2026-11-16

---

## Contexto

Em arquiteturas distribuídas, é comum questionar a inclusão de um **API Gateway** (como YARP, Ocelot ou Kong) à frente das APIs de negócio. O requisito não-funcional do desafio informa que, em momentos de pico, o serviço recebe **50 requisições por segundo (50 RPS)** com tolerância máxima de 5% de perda.

## Decisão

Decidimos **não implementar um processo/container de API Gateway** na camada de aplicação, expondo diretamente a `CashFlow.Api` (com autenticação JWT e Rate Limiting nativos do ASP.NET Core 8).

## Justificativa Técnica e Matemática

1. **Capacidade Nativa do Kestrel (.NET 8) vs. 50 RPS (Lei de Little):**
   - **50 requisições por segundo** equivalem a **1 requisição a cada 20 milissegundos**.
   - Pela Lei de Little ($L = \lambda \times W$), considerando uma latência média de resposta de $W = 5\text{ ms}$ ($0,005\text{ s}$) para ler um saldo pré-computado no Redis/PostgreSQL ou enfileirar um lançamento no RabbitMQ:
     $$L = 50 \times 0,005 = 0,25 \text{ requisições simultâneas em voo}$$
   - Uma única instância enxuta do ASP.NET Core 8 (Kestrel) suporta facilmente **dezenas de milhares de requisições por segundo** em operações em memória/cache e **milhares de requisições por segundo** em consultas SQL indexadas — ou seja, possui **folga de 50x a 500x** sobre o requisito de 50 RPS sem necessidade de camadas intermediárias.

2. **Eliminação de um Ponto Único de Falha (SPOF) e Latência Extra:**
   - Adicionar um container de API Gateway na frente da aplicação introduz um *network hop* adicional (serialização/deserialização ou proxy reverso) e mais um componente que pode falhar.
   - Como unificamos os endpoints HTTP (`POST /api/v1/transactions` e `GET /api/v1/daily-balance/{date}`) em uma Web API leve e desacoplamos o processamento pesado em Workers assíncronos (`TransactionWorker` e `ConsolidationWorker`), um API Gateway seria puro *over-engineering* (violando **KISS** e **YAGNI**).

## Consequências

- **Positivas:**
  - Menor latência ponta a ponta.
  - Menor complexidade operacional e menor consumo de memória/CPU nos ambientes local e produtivo.
- **Evolução em Produção (Cloud):**
  - Caso a aplicação rode em Kubernetes (EKS/AKS) ou Cloud gerenciada, o balanceamento de carga L7 e terminação TLS são delegados nativamente ao **Ingress Controller (NGINX / AWS ALB)** da própria infraestrutura, sem necessidade de manter código customizado de Gateway.
