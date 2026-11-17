# ADR-005: Segurança — Autenticação JWT, Autorização, Criptografia e Rate Limiting

- **Status:** Aceito
- **Data:** 2026-11-16

---

## Contexto

O desafio exige contemplar mecanismos de segurança:
> *"Segurança: Proteja os dados e sistemas contra ameaças. Implemente autenticação, autorização, criptografia e mecanismos de proteção contra ataques."*

Ao mesmo tempo, a solução precisa ser facilmente executada e testada localmente pelos avaliadores sem depender de configuração manual de um provedor externo (como Keycloak, Auth0 ou Azure AD/Entra ID).

## Decisão

1. **Autenticação e Criptografia com JWT Assinado (`HMAC-SHA256`):**
   - Implementamos o endpoint `POST /api/v1/auth/token` na própria API (simulando um *Identity Provider* OAuth2/OIDC) para emissão de tokens JWT assinados criptograficamente com algoritmo `HS256` (`HMAC-SHA256`) e tempo de expiração controlado.
   - Todos os endpoints de negócio exigem o envio do header `Authorization: Bearer <token>`.

2. **Autorização Baseada em Roles e Scopes (Claims):**
   - Segregamos as permissões em duas políticas de autorização explícitas:
     - `CanWriteCashFlow`: exige usuário autenticado com role `Merchant` e claim `scope = cashflow.write` para criar lançamentos (`POST /api/v1/transactions`).
     - `CanReadCashFlow`: exige usuário autenticado com role `Merchant` e claim `scope = cashflow.read` para consultar o saldo (`GET /api/v1/daily-balance/{date}`).

3. **Proteção Contra Ataques (Rate Limiting e Validação Estrita):**
   - **Rate Limiting (`Microsoft.AspNetCore.RateLimiting`):**
     - `AuthPolicy`: janela fixa restrita no endpoint `/api/v1/auth/token` para mitigar ataques de força bruta (*Brute Force*) e *Credential Stuffing*.
     - `ApiPolicy`: janela fixa dimensionada acima do pico de negócio (200 req/s por instância, 4x acima do pico de 50 req/s exigido), protegendo os serviços contra ataques de negação de serviço (DoS) e retornando `429 Too Many Requests` quando excedido.
   - **Validação de Entrada (`FluentValidation` + Domínio Rico):**
     - Rejeição imediata na borda (`400 Bad Request`) de valores negativos/zero, datas inválidas ou descrições fora do limite permitido.

## Consequências

- **Positivas:**
  - Cobre autenticação, autorização, assinatura criptográfica e proteção contra abuso/DoS com componentes nativos e de alta performance do .NET 8.
  - Facilidade imediata de avaliação via Swagger UI ou `curl`.
