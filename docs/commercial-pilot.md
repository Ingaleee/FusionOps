# FusionOps Planning Intelligence: commercial experiment

Product analysis of the supplied business proposal, 2026-10-13. This is an unvalidated commercial hypothesis alongside the [full engineering programme](ultimate-architecture-vision.md). No customer demand, saved money, paid pilot, production deployment or real ERP integration is claimed. The initial business experiment uses the same C# domain/planning/authority core; it does not reduce G1–G30 to a planning-only product.

## First workflow and customer hypothesis

Explore industrial installation and equipment-service teams that repeatedly reassign specialists, equipment and parts across parallel projects. The proposed user is a planner/dispatcher; the reviewer is an operations or project lead; the potential buyer is the person accountable for delivery and resource economics. Confirm those roles with actual prospective users.

The proposed first experience is: import permitted operational data; inspect mapping/quality errors; calculate validated alternatives; compare resource conflicts, dates and modeled costs; review a plan; export a versioned proposal back to the existing process. Begin with read/simulation and explicit exports. Writing into a customer's authoritative ERP is a later adapter capability with approval, stable effect identity and reconciliation.

This is a focused operational-planning layer over existing systems. It requires a defined authority map: for each record, name whether the source ERP or FusionOps owns its identity, revisions, capacity, approval and mutations. Imported mirrors must not masquerade as reserved resources. When an ERP owns capacity, FusionOps acceptance requires that provider's tested authoritative protocol; a local reservation alone cannot establish real external ownership.

## Capabilities the pilot needs

| Capability | Concrete contract / evidence |
| --- | --- |
| Import staging | Schema version, source/tenant/external ID mapping, row errors, units/currency/time-zone rules and source timestamps; immutable imported revisions; idempotent retries; no caller-supplied replacement of trusted catalog rates |
| Data-quality review | Missing skills/windows/costs, inconsistent dependencies, expired observations and unsupported requirements are visible before solving; no silent zero-price or unlimited-capacity defaults |
| Planning jobs | Durable bounded submission/cancellation, job/result retrieval, per-tenant admission and retained computation usage; existing synchronous API is not yet this job service |
| Planning Studio | Graph/resource calendar, shortage reasons, alternatives, source freshness, cost/finish/disruption diff, reviewed-version approval and export provenance |
| Comparison protocol | Preserve the client's original feasible schedule; run alternatives against the same source revision, constraint model, candidate set, budget and solver limits; independently validate both |
| Adapter proof | One deterministic provider simulator plus one chosen real adapter only when accessible and authorized; signed inbound messages and replay/unknown-outcome contract tests |
| Pilot report | Dataset permissions/anonymization, revisions, experimental design, workload/environment, modeled outcome, observed user result, costs and limitations |

The example API request in the proposal is conceptual. It does not create a new public unauthenticated endpoint or authorize bypassing tenant/project access. Reuse registered project/scenario contracts and expose jobs/adapters through their owning modules.

## Customer discovery and pilot gates

1. Prepare a discovery guide for 10–15 potential users: recent scheduling incident, affected commitments, current tools, time spent, authority to change reservations, data availability and expected integration work. This is a proposed sample, not completed interviews. Outreach requires the owner's explicit instruction.
2. Obtain an authorized, anonymized dataset and document constraints/cost definitions with the planner. Start from a fixed historical snapshot; avoid hindsight leakage when comparing decisions.
3. Agree pilot metrics and acceptance thresholds with the customer before seeing results. Run shadow planning first, retain both baseline and proposed schedules, and have a domain user review applicability.
4. Report modeled improvements separately from observed operational outcomes. If a prospective plan is used, retain approval, executed changes, deviations, adoption and integration/support costs.
5. Consider a paid pilot or repeatable offering only after the customer confirms utility and authority/data requirements. Pricing and scope remain negotiation hypotheses, not a published tariff.

No message, contract, customer-data upload, billing action or production write is authorized by this document.

## Metrics and economic interpretation

| Metric | Definition / guard against misleading results |
| --- | --- |
| Planning effort | User preparation/review time and compute time are reported separately; include fixing input errors and reviewing proposed changes |
| Resource conflict | Independently detected hard-constraint violations per comparable plan; exclude solutions that trade an unapproved constraint for a lower cost |
| Commitment delay | Actual or modeled lateness by project/milestone under the same assumptions; distinguish missed contractual dates from arbitrary utilization targets |
| Resource cost | Same currency/rates/work rules; report modeled labor/equipment/material cost separately from realized cash or margin |
| Utilization | Define available, booked, productive and billable hours; raising occupancy alone is not proof of value |
| Change burden | Number and magnitude of changed assignments/intervals, travel/setup and human review; a cheaper plan may be impractical to adopt |
| Integration cost | Initial mapping/adapter/configuration effort plus ongoing operation/support; include this when evaluating benefit |
| Adopted outcome | Track proposals used/rejected and reasons; solver feasibility is not proof that people executed the plan |

The supplied illustration is arithmetically correct: `50 specialists × 160 hours × 0.03 = 240 hours`; `240 × EUR 50 = EUR 12,000`. This is an assumed opportunity, not measured FusionOps savings. Show cash cost avoided, incremental contribution margin and unmonetized capacity separately. Do not count a freed hour both as salary savings and new revenue. Compare realized attributable benefit with implementation, subscription, operation and change-management costs, and disclose uncertainty.

## Market and licensing checks

Official product pages checked on 2026-10-13 show that the proposed feature category has established alternatives. This supports a competitor comparison; it does not prove demand for FusionOps or a unique market gap.

- [Microsoft Dynamics 365 Project Operations pricing](https://www.microsoft.com/en-us/dynamics-365/products/project-operations/pricing) lists project scheduling, budgets, resourcing and execution. The US page displays USD 135 per user/month paid yearly, with regional-price caveats and separate Copilot credits. This is another product's advertised price, not evidence for FusionOps pricing.
- [Oracle Primavera Cloud](https://www.oracle.com/construction-engineering/primavera-cloud-project-management/) describes shared resource planning and project/portfolio risk management; its [risk documentation](https://primavera.oraclecloud.com/help/en/user/88433.htm) covers impacts on schedule and cost.
- [Odoo Planning](https://www.odoo.com/app/planning) describes staff/equipment planning and automated schedules.
- [Planview scenario planning](https://www.planview.com/lp/scenario-planning/) describes comparing alternatives and connecting decisions with portfolio execution.

Do not claim that these products cannot recover effects, optimize a particular constraint or satisfy a customer's workflow without a tested comparison. Explore a narrower integration burden and concrete planning incident as the differentiator to validate.

No repository license file was found during this source review. [GitHub's licensing guidance](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository) distinguishes public visibility from granting an open-source license. The owner must choose the distribution strategy and verify code/dependency rights before external licensing; this analysis does not install a license or change those rights. SaaS, API licensing, private deployments and integration services are alternative commercial hypotheses. Selecting/supporting all four immediately would add product work without customer evidence.

## Near-term architecture consequences

Continue G3 approved temporal execution, then inventory/adaptive cost and tenant operation. Design source identity/provenance, versioned policies, deterministic exports and bounded jobs so a later importer or ERP adapter can use the same verified core. Keep source mirrors, proposals, accepted ownership and actual facts distinct. Complete engineering demonstrations with protocol simulators while independently validating prospective customer demand; neither track can substitute for the other's evidence.
