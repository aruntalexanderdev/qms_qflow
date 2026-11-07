# ADR-006 — First deployment on Azure

Status: Accepted (platform: Azure). Service choices below are Proposed.
Date: 2026-11-07
Story: — (Phase 0 decision by product owner); infrastructure stories in phase 1D

## Context
The product owner chose Azure for the first deployment. The spec requires customer data to stay in-country, HTTPS only, Docker containers, and no external CDN calls from displays or dispensers. The load target is 50 branches × 20 counters.

## Decision
| Concern | Azure service |
| --- | --- |
| Region | An Azure region inside the bank's country (e.g. UAE North for a UAE bank); all resources and backups stay there. **Confirm with bank IT.** |
| API | Azure Container Apps: `qms-api` (internal ingress), .NET 10 image |
| Web + displays | Azure Container Apps: `qms-web` (external ingress, custom domain + managed certificate), an Nginx image that serves the Angular build and proxies `/api` and `/hubs` to `qms-api`. One origin, so no CORS, and the refresh cookie works (ADR-004). |
| Database | Azure Database for PostgreSQL Flexible Server (PostgreSQL 16+), private access through a VNet, zone-redundant HA in production, geo-backup off (data residency) |
| Secrets | Azure Key Vault (JWT signing key, connection string, phase 2 provider credentials), read through managed identity |
| Images | Azure Container Registry, pulled through managed identity |
| Monitoring | Application Insights + Log Analytics through OpenTelemetry; Serilog to console |
| Real-time | Phase 1: `qms-api` runs as a single replica (enough for the load target) with session affinity on. To scale out, add Azure SignalR Service (if available in the region) or a Redis backplane. |
| CI/CD | GitHub Actions → ACR → Container Apps, authenticated with OIDC federated credentials (no stored secrets) |
| Infrastructure as code | Bicep in `infra/` |
| Environments | `dev`, `uat`, `prod`, each its own resource group |
| Migrations | An EF Core migration bundle run as a Container Apps job before each release, not at API startup |

On-premise stays possible: the same images run with `docker compose` behind Nginx.

## Consequences
- Managed TLS, scaling, backups and monitoring; no VMs to patch.
- Branch devices need outbound HTTPS to the public web endpoint. Bank IT may require IP allow-listing or a VPN; Container Apps ingress supports IP restrictions.
- The single API replica is a known scale limit, documented with a clear upgrade path.

## Spec impact
Sections 2, 3.1, 11 and 13 updated.
