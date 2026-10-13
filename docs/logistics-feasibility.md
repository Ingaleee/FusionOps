# Vehicle-carrier logistics feasibility research

Status: research, 2026-10-13. No transport optimizer, real carrier integration, proven loading plan, TMS or logistics customer is claimed. The [product strategy](product-strategy.md) retains approximately 15% research attention until G7 while the full Core programme continues. This document defines a testable hypothesis and explicit model gaps.

## Candidate decision

Investigate whether a carrier's dispatcher would benefit from comparing feasible assignments of vehicle-transport orders to available tractor/trailer/driver combinations, pickup/delivery windows and loading configurations. Start from a permitted historical dataset and a fixed customer baseline. Do not assume route length is the customer's principal cost: identify empty travel, waiting, loading work, missed windows, changed commitments and implementation burden separately.

## Proposed model

| Entity / input | Required information |
| --- | --- |
| Transport order | External/source identity, cargo vehicle characteristics and dimensions/mass, pickup/delivery pair, service windows/durations, priority, committed terms, cancellation and handling restrictions |
| Equipment combination | Tractor/trailer compatibility, certified layout/configurations, deck/position limits, maintenance, current location, availability and actual capacity evidence |
| Driver | Qualification/authorization, assigned equipment, location, availability and market-specific duty/break constraints with a reviewed rule version |
| Stop / travel matrix | Locations, directed travel duration/distance/cost, timestamp/model version, departure assumptions and uncertainty; coordinates alone do not establish a drivable route |
| Loading witness | Permitted cargo/position/configuration assignments, required unloading order, loading/unloading actions and checked physical limits |
| Cost / commitment | Currency, price/cost basis, loaded/empty travel, waiting/handling, retained promises and authorized penalties; unknown inputs remain unknown |
| Actual facts | Accepted order, loading/collection/delivery confirmation, partial/cancelled/uncertain result, observed/effective time and original event identity |

The candidate planning problem combines pickup-and-delivery routing with time windows, equipment/driver scheduling and configuration-dependent loading. A vehicle-count capacity alone is an intentionally simplified research model; it cannot certify that mixed cargo physically fits or can be unloaded in route order. Loading may require a finite set of manufacturer/operator-approved configurations or a separate physical validator; choose with a carrier/equipment expert and data, not an invented universal geometry rule.

Hard constraints proposed for the experiment: pickup precedes delivery on the appropriate equipment; each accepted order is served once; all stop windows and travel/service times fit; equipment/driver ownership does not overlap; eligibility and configuration checks hold; capacity/physical constraints are satisfied; confirmed work and irreversible confirmations are retained. Include optional/priority work only under an explicit policy. Budget, cost, deadline and disruption objectives must state their order and units; a cheaper route does not revise a customer's promise.

Operating jurisdictions, certified equipment, routing-provider rights and exact driving/weight/dimension rules are unknown. Populate versioned approved constraints for the chosen pilot before declaring operational feasibility. This research supplies no numerical legal or engineering limits and no claim of roadworthiness.

## Reuse and gaps

| Existing or planned Core mechanism | Reuse | Gap to resolve |
| --- | --- | --- |
| Project definitions/scenarios | Versioned inputs, calendars, identities, immutable source evidence and comparison | Current task DAG is not a route model; travel and pickup/delivery/configuration contracts are absent |
| Resource bundles | Concurrent driver/equipment requirement and eligibility vocabulary | Travel-dependent availability, carrier combinations, duty rules and certified loading differ from static resource slots |
| Independent validation | Validate a decision separately from optimization, retain model/solver limits | Need a route validator and loading witness; current temporal validator cannot certify either |
| Reviewed baseline / future activation | Exact digest approval, ownership and durable effects | Transport-specific order/dispatch/provider acceptance and irreversibility contracts must be implemented and tested |
| G4/G5 supply/facts/cost | Typed inbox, unknown-outcome reconciliation, actual facts and immutable cost postings | They remain incomplete Core stages; cannot be assumed available for a transport pilot |
| Tenant jobs/policies | Isolation, cancellation, admission and controlled changes | G6 fair admission and tenant-configurable policies still remain future work |

Preserve generic Core terms and express transport-specific entities in a separate module with explicit owning interfaces. Do not turn every project into a shipment or treat loading feasibility as an equipment type string.

## Experiment gates

1. **Discovery:** a permitted carrier workflow, decision maker, baseline, equipment types, jurisdictions and data quality are documented. No such discovery is reported complete.
2. **Static routing experiment:** retain a finite travel matrix; solve small pickup/delivery/window instances; compare with an independent exhaustive oracle and independent validator. Explicitly exclude unmodeled loading, traffic and duty guarantees.
3. **Loading experiment:** validate candidate cargo/configuration/loading-order witnesses against approved equipment data. A route rejected by the physical validator stays rejected, even when cheaper.
4. **Combined shadow experiment:** compare route plus loading plus driver/equipment constraints on historical data with no external writes. Report feasible rate, lateness/cost/disruption, search budget and computation/review effort. Avoid hindsight leakage.
5. **Operational module decision:** promote only a verified customer need with an agreed adapter/approval/recovery scope. Lost dispatch/confirmation responses require original-effect reconciliation, not a second transport instruction.

Evidence bundle: dataset permission/schema/revision, equipment/rule/travel versions, baseline, objective, time/node budget, solver identity/seed, loading and route validation, explanation/diff, measured outcomes and exclusions. Stop or revise the hypothesis if data/physical rules cannot support a usable decision; this does not stop Core development.

## Primary references and limits

[Google OR-Tools VRPTW documentation](https://developers.google.com/optimization/routing/vrptw) demonstrates routing with travel-time matrices and stop windows. [Its pickup/delivery example](https://developers.google.com/optimization/routing/pickup_delivery) demonstrates paired pickup/delivery constraints, same-vehicle assignment and precedence. These are useful comparison models for a later experiment; they do not validate car-carrier geometry, market demand, legal operation or FusionOps integration. No dependency or production solver is selected by this research.
