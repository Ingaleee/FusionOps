# ADR-007: Identity Provider Choice — Keycloak vs Azure AD

Date: 2024-05-01

## Context
The platform needs an OAuth2/OIDC provider for authentication and role-based authorization.
Two candidates:
1. **Keycloak** (open-source, self-hosted).
2. **Azure AD B2C** (managed cloud service).

## Decision Drivers
* On-prem deployments for enterprise clients.
* Free/community license (no per-MAU cost in dev sandboxes).
* Extensible (custom user federation, theme, SPI providers).
* GitOps/k8s friendly (Helm chart, backup via PVC).
* Alignment with other FOSS stack components (Postgres, RabbitMQ, Kafka).

## Decision
Choose **Keycloak**.

## Consequences
+ Full control over realm, themes, custom claims, token lifespan.
+ Works offline in air-gapped environments.
+ One more service to operate (DB, upgrades, HA).
+ Later we can add Azure AD / Google as external identity providers for SSO.  
  No change to API — only OIDC discovery URL. 