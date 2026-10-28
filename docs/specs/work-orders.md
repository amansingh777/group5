# Spec: Work orders and engineer scheduling

Status: Draft for sign-off. Source: `docs/stakeholder-brief.md` (Daniel Mensah, Head of Field Operations). Date: 2026-10-27.

## Overview
Field Operations plans engineer visits today in a spreadsheet. A "job" in the system is only a row, with no visit history, no real double-booking check and no link to the stock used on a visit. This spec moves that planning into the system as a **work order**: a customer request for work at a site, carried through allocation, booking, the visit itself and a final outcome. It adds a suitability check on skills, a no-double-booking rule with one exception for emergencies (P1), a "next free slot" lookup, visit outcomes with parts and stock kept accurate, a first-time-fix report, and a dispatcher board.

Two parts of the brief cannot be built as written. Automatic ordering from the supplier is replaced by a manual version. Recording of customer calls without telling the customer is not built at all. Both are explained in the Findings section.

Pricing, invoicing, customer self-service and the monthly utilisation report are out of scope. The utilisation report is a separate ticket and must not be changed by this work.

## Delivery status

Checked against the code on branch `feature`, 2026-10-27. Where the code differs from a rule below, the difference is listed under "Reality differs" in the Business rules and in the reconciliation notes at the end of this section.

**Built**
- Lifecycle and status changes: `GET /api/work-orders`, `GET /api/work-orders/{id}`, `POST /api/work-orders/{id}/status`. The status endpoint itself offers only Requested (Raised) to Triaged and any open status to Cancelled. Allocated, Scheduled, OnSite and Completed are reached through the allocate, schedule and visit endpoints. Each status change is written as a job note with the user name and the UTC time.
- Allocation: `GET .../candidates` and `POST .../allocate`. Hard blocks for a missing required skill and an inactive engineer (422). Warnings for a different region and for no shift on the day. There is no workload warning.
- Scheduling: `POST .../schedule` with the overlap rule (409), P1 displacement of non-P1 bookings that have not started (the displaced job goes back to Allocated with its booking cleared and a note), and engineer then job row locks. `GET .../next-slot` (14 days, shift time less the break block, default job length). Times are UK wall-clock: a start in the past is refused with 422 (a P1 gets 15 minutes of grace), and next-slot never offers a start before UK now. A P1 locks each job it displaces and re-checks it, so it never overwrites a job that started in the meantime (409), and a one-for-one swap on a full day is allowed.
- Visits: `POST .../visits/start`, `POST .../visits/complete` (outcome, notes, parts), `GET .../visits`. Any outcome other than Fixed returns the work order to Triaged and creates an unscheduled follow-up visit.
- First-time-fix report: `GET /api/reports/first-time-fix`, a separate calculation from the utilisation report.
- Dispatch board: `GET /api/dispatch/board?from&days&regionId`, with amber and red SLA states and the "Needs rebooking" flag. The unscheduled list is capped at 200 (needs-rebooking first, then P1 to P3, then SLA due date); `unscheduledTotal` gives the full count and the page shows "Showing N of TOTAL" when it is truncated.
- Parts stock safety: conditional stock decrement (409 when there is not enough stock, nothing changes). Manual reorder: `GET /api/reorder-requests`, `POST /api/reorder-requests/{id}/ordered`, `POST /api/reorder-requests/{id}/received`. A request is created when a part is used and its stock is at or below the reorder level. Migration 008 backfilled Open requests for DV-WB-01 and HP-CTRL.
- Frontend pages: `/work-orders`, `/work-orders/:id`, `/dispatch`, `/reports/first-time-fix`, `/reorder`.

**Test status.** The automated suite was last run by the repository owner on 2026-10-28, after the final round, and reported passing ("test successful"). No pass or fail counts were reported to the delivery team. The round-3 tests were written without being executed by the delivery agents.

**Not built**
- Call recording (N2). Must not be built. No code, schema or endpoint exists for it, and none may be added under this spec.
- Supplier integration (N1). Deferred until the supplier has named its interface (Q14).
- The booking warning for a part that is not coming (R29). Not built, because jobs have no required-parts data. See Q18.
- The "no engineer free" warning on schedule (Q16). Not built.
- Authentication and audit identity (G10, Q12, Q13). Not built. The `userName` on each request is recorded, not verified.
- `POST /api/work-orders`. Not built as an endpoint. Work orders are created through the existing job paths: `POST /api/jobs` and the CSV import (`POST /api/imports/job-requests`).
- `NotCompleted`. Reserved in the status set, but no code path sets it. A non-Fixed outcome returns the work order to Triaged (R18).

**Reconciliation notes (spec and code disagree; not yet decided)**
- Allocation is accepted from Requested as well as Triaged. R2 has no Raised to Allocated move.
- Legacy `POST /api/jobs` and `PUT /api/jobs/{id}` used to bypass the work-order rules. After the code review they apply the same hard skill and inactive-engineer rule as allocate and schedule (422, no override) and the same non-P1 overlap rule (409), under the engineer and job row locks. A P1 overlap through these endpoints is refused with 409 and points to the schedule endpoint. `PUT` accepts only statuses in the work-order set (`InProgress` is now 400), requires an engineer for Allocated, Scheduled and OnSite and both times for Scheduled and OnSite, and refuses status, engineer or time changes on a job that already has visits (409). The checks run only when the engineer, times or status actually change, so unrelated edits are not blocked. `PUT` now refuses a status change into OnSite, Completed or NotCompleted (409, naming the visit endpoints; those states are reached only through `POST .../visits/start` and `.../visits/complete`), and both `POST` and `PUT` reject half-supplied scheduled times (400: give both a start and end, or neither, and the end must be after the start). `PUT` can still move a job between the other statuses without following R2 step by step, for example from Requested straight to Scheduled when an engineer and both times are supplied.
- Reorder requests are created only when a part is used (`POST /api/jobs/{id}/parts` or a visit completion). A stock change made through `PUT /api/parts/{id}` does not create one, though R27 and AC-15 say "when the stock change is saved".
- `POST /api/jobs/{id}/parts` decrements stock without a visit. R19 assumes parts are recorded against a visit.
- The next-slot tie-break (R16) uses same region, then name, then id. It does not apply the full R7 ranking.
- Stakeholder agreement: Daniel Mensah agreed to the delivery team's recommendations on the contradictions (C1 to C3), the gaps (G1 to G10, including the proposal given in each), N1 (manual reordering now, supplier integration later) and N2 (call recording is not built). This was reported to the delivery team on 2026-10-27 and is recorded here on that report, not from a written reply from Daniel. His answers to the numbered open questions are not recorded, so they remain Open.

**Decisions needed from Daniel (consolidated)**
- Q1 to Q17 as listed in the Assumptions table. All are still "Open" in that table.
- Q18 to Q23 added in this revision (parts on a work order, cancelling an On-site job, the reorder trigger, spend approval, who may do what, first-time-fix history).

## Findings

Each finding says what we found, what we recommend, and what the stakeholder must decide. Question numbers (Q1 to Q15) match the Assumptions & Open Questions table.

### Contradictions

**C1. "Any engineer on any job" versus "a boiler job must never go to someone without a gas qualification."**
- What we found: The brief asks for dispatchers to be free to put any engineer on any job with no screen saying no. It also says a boiler job must never go to an engineer without a gas qualification. Both cannot be true at once. The codebase does hold the required skill for each job type (`JobType.RequiredSkillId` and `EngineerSkill`), so the gas rule can be enforced.
- What we recommend: Make the required-skill check a hard block that nobody can override, including managers. Leave all other checks (region, distance, workload) as warnings that the dispatcher can accept.
- What the stakeholder must decide: Confirm the gas rule is absolute (Q1). Confirm which other checks may only warn (Q1).

**C2. "Never double-booked, ever" versus "a P1 emergency always goes on straight away, even mid-job, and the other job moves."**
- What we found: Both rules cannot hold in the same case. A P1 booked over an engineer's existing job is a double-booking, unless the other job is moved first. The brief does not say what happens to the moved job or who is told.
- What we recommend: Block overlaps for all non-P1 bookings. A P1 may take a slot that is already booked. The job that is moved is returned to Allocated with its booking cleared, flagged as needing a rebook, and recorded in the job's notes. It is never silently dropped. An "On site" job is not interrupted unless the stakeholder says so.
- What the stakeholder must decide: Who is told when a job is moved, including whether the customer is told (Q2). Whether one P1 may move another P1 (Q2). Whether a job already "On site" may be interrupted (Q2).

**C3. "Customers must not be told" about call recording versus "some will ask us to stop."**
- What we found: The brief asks that customers are not told about recording, while also expecting that some customers will ask for it to stop. A customer who asks to stop has a right to be told about the recording and to object. Keeping the recording hidden would defeat that right.
- What we recommend: This cannot be resolved as written. See N2. No recording feature is specified here.
- What the stakeholder must decide: See N2 (Q15).

### Gaps (answers needed before build)

**G1. Status names are "whatever you think best".**
- Found: The brief lists the steps as raised, triaged, allocated, scheduled, on site, then finished (completed, not completed, cancelled). The existing `Job.Status` is a free text field with the default "Requested" and no transition rules (`JobService.UpdateAsync` sets it directly).
- Recommend: Use Raised, Triaged, Allocated, Scheduled, OnSite, Completed, NotCompleted, Cancelled, with the allowed moves in Business rules R2.
- Decide: Confirm the names and who may move a work order between states (Q3).

**G2. "Nearest engineer" cannot be measured.**
- Found: Customers and sites hold a postcode and a region only. There are no coordinates, so true distance cannot be computed.
- Recommend: Use a proxy ranking: same region first, then same postcode outward code, then fewest booked hours that day. True distance needs a geocoding service, which costs money and needs a supplier choice.
- Decide: Accept the proxy for version 1, or approve a geocoding provider and its cost (Q4).

**G3. "Suitable" engineer depends on skill codes that may not match the job types.**
- Found: Skills are a code and a name. The brief mentions gas, meter, heat pump and others. We have not checked that every job type maps to the right skill in the seed data.
- Recommend: Check the seed data against a written list of job types and skills before build.
- Decide: Confirm the job type to skill mapping (Q5).

**G4. Visit outcomes do not match the existing outcome values.**
- Found: The brief lists four visit outcomes: fixed, parts needed, no access, follow-up required. The existing job outcome is a free text value, and only "Fixed" has a meaning in code (`JobService.cs:96`). The brief does not say whether a return visit is created automatically.
- Recommend: Use the four outcomes in R17. Leave the work order open for a return visit after any outcome other than Fixed.
- Decide: Confirm the mapping and what each outcome does to the status, including whether a return visit is created automatically (Q6).

**G5. First-time-fix is not defined.**
- Found: The current flag is simply "outcome is Fixed" (`JobService.cs:96`). It does not know about return visits, because the codebase has no visit records. One job equals one visit today. The brief does not say whether "no access" is a failed first visit, whether a follow-up booked within some number of days is a return visit, or which date places a job in a month.
- Recommend: Count a work order as first-time-fixed only if its first visit was Fixed and no later visit exists (R21). Place it in the month of its first visit.
- Decide: Confirm the rule, the "no access" treatment and the date used (Q7).

**G6. "Near an SLA" has no threshold.**
- Found: The SLA due time is set on creation: P1 4 hours, P2 24 hours, otherwise 72 hours (`JobService.cs:74`). There is no "near" value.
- Recommend: Amber when less than 25% of the window is left, red when breached.
- Decide: Confirm the 25% threshold (Q8).

**G7. "Next slot" has no search rules.**
- Found: The brief asks for one call that returns the next free slot. It does not say how far ahead to look, whether only shift hours count, whether travel time counts, or how long a job takes. Job length is available as `JobType.DefaultMinutes`.
- Recommend: Search 14 days ahead. Use shift time only, less break minutes. Use `DefaultMinutes` as the job length. No travel time in version 1.
- Decide: Confirm these values (Q9).

**G8. "Van or stores stock" cannot be represented.**
- Found: A part has a single stock figure (`Part.StockQty`). There are no stock locations, so van stock and stores stock cannot be told apart.
- Recommend: Keep one stock figure in version 1. Add a stock location table only if the business needs it.
- Decide: Do we need two stock locations, and who counts stock (Q10)?

**G9. Scale and region coverage are unconfirmed.**
- Found: The brief says ten engineers across six regions. Each engineer is held against one region only (`Engineer.RegionId`).
- Recommend: Build for one region per engineer. Confirm the number of engineers and whether anyone covers more than one region.
- Decide: Confirm the scale and the one-region rule (Q11).

**G10. Roles are not defined.**
- Found: The brief names dispatchers, engineers and the Head of Field Operations. The codebase has no authentication and no enforced roles. A `User` entity with a `Role` text field exists, but nothing checks it.
- Recommend: Define three roles (dispatcher, engineer, field operations manager) and record the name of the person who made each change. Full login is a separate piece of work.
- Decide: Confirm the roles and what each may do (Q12). Confirm the minimum identity needed now (Q13).

### Cannot be built as the codebase stands

**N1. Automatic reordering from the supplier's ordering system, with an arrival date shown.**
- What we found: There is no supplier record, no supplier login or API details, no purchase order record and no expected arrival date anywhere in the system. We do not know the supplier's interface. Also, the brief promises that a job is never booked against a part that is not coming. That promise depends on the supplier's data, which we do not control.
- What we recommend: Build the manual version now. The system creates a reorder request when stock falls to the reorder level (`Part.ReorderLevel` exists). Staff record "ordered" and the expected date by hand. A booking shows a warning when a part needed for the work has no stock and no expected date. Supplier integration is a later phase, once the supplier has named its interface.
- What the stakeholder must decide: The supplier's name and whether it has an API (Q14). Who approves spend, since ordering is a financial commitment and pricing is out of scope (Q14). Whether a warning is enough, or booking must be blocked (Q14).

### Must not be built as written

**N2. Covert recording of dispatcher and customer calls, kept for two years, with customers not told.**
- What we found: Recording a call without telling the caller is unlawful in the UK. It breaks the transparency duties under UK GDPR, and interception rules under PECR and the Investigatory Powers Act may also apply. The brief's own note that some customers will ask to stop shows that the right to object would be frustrated. The system has no telephony at all, so recording would also need a telephony supplier and a Data Protection Impact Assessment (DPIA).
- What we recommend: Do not build recording. This spec contains no call recording rules, API or acceptance criteria. As an alternative, dispatchers can log call notes against the work order using the existing job notes. If recording is still wanted, it is a separate project that needs: a disclosure at the start of each call, a stated lawful basis, opt-out handling, a written justification for two years of retention, access controls, and a DPIA signed off with the company's Data Protection Officer.
- What the stakeholder must decide: Drop recording, or commission the compliant version (Q15). A written answer is needed before any recording work starts. The Data Protection Officer must sign off.

### Out of scope
- Pricing and invoicing.
- Customer self-service.
- Any change to the monthly utilisation report (`ReportService`). It is a separate ticket. The new first-time-fix report is a separate method only.
- Call recording (N2).
- Automatic supplier ordering (N1), beyond the manual version in this spec.

## Feasibility

| Requirement | Verdict | Notes |
|---|---|---|
| Work order lifecycle with named statuses | Feasible | Extend the status set and add transition rules. New migration `006`. |
| Suitable-engineer allocation with a hard gas rule | Feasible | Uses `JobType.RequiredSkillId` and `EngineerSkill`. Depends on G3 mapping. |
| Nearest engineer | Partly | Region and postcode proxy only. True distance needs geocoding (Q4). |
| Dispatcher can override non-safety checks | Feasible | Warnings returned. The skill rule cannot be overridden. |
| No double-booking | Feasible | Overlap check on engineer and time window, in one transaction. |
| P1 pre-empts and moves a booking | Feasible with rules | Needs the C2 decisions (Q2). |
| One call returns next slot and engineer | Feasible | New endpoint using rostered shifts and existing bookings. Rules per Q9. |
| Visit outcomes and parts, with accurate stock | Feasible | Needs a visit record and a stock decrement that cannot go negative. This also fixes the known gap in `AddPartAsync`. |
| Van versus stores stock | Partly | Needs stock locations (Q10). |
| First-time-fix dashboard by region and job type | Feasible after G5 | Needs visit history. The existing flag is wrong for return visits. |
| Dispatcher board with SLA flags | Feasible | New page: engineers down the side, days across the top. |
| Automatic supplier ordering with arrival date | Not as stated | See N1. The manual version is feasible. |
| Call recording kept two years, customers not told | Must not be built | See N2. |
| Login for dispatcher actions and an audit trail | Partly (prerequisite gap) | No authentication exists. Overrides and moves need at least a named user (Q13). |

## Business rules

Only the feasible parts are covered. Rules that depend on an open decision carry the question number in brackets.

**Work order lifecycle**
- R1. A work order has one of these statuses: Raised, Triaged, Allocated, Scheduled, OnSite, Completed, NotCompleted, Cancelled. In the code, Raised is stored as `Requested` (the existing `Job.Status` value used by `JobService.CreateAsync` and the importer). [Q3]
- R2. A new work order starts at Raised (stored as `Requested`). Allowed moves are: Raised to Triaged, Triaged to Allocated, Allocated to Scheduled, Scheduled to OnSite, OnSite to Completed or NotCompleted, and any open status to Cancelled. Any other move is refused with error 409. [Q3]
  - Reality: `POST /api/work-orders/{id}/status` accepts only Requested to Triaged and any open status to Cancelled. The other moves happen through the allocate, schedule and visit endpoints (see the API table). Allocate also accepts Requested, which R2 does not allow (see the reconciliation notes in Delivery status).
- R3. Completed, NotCompleted and Cancelled are final. A Completed work order cannot be cancelled (the existing rule in `JobService.CancelAsync`).
- R4. Every status change records the time and the name of the user who made it. The user name comes from the minimum identity chosen in Q13. [Q13]
  - Reality: the change is written as a job note (author = user name, note text includes the previous and new status and the UTC time). There is no separate audit table. The name is recorded, not verified (Q22).

**Allocation**
- R5. A work order can only be allocated or booked to an engineer who holds the skill required by its job type. This is a hard block, with error 422. Nobody can override it, including managers. [Q1]
- R6. An inactive engineer (`Engineer.Active` false) cannot be allocated. Error 422.
- R7. The system offers a ranked list of suitable engineers, in this order: same region as the work order, then same postcode outward code, then fewest booked hours that day, then name. [Q4]
- R8. A dispatcher may allocate any active, suitable engineer, including one who is not at the top of the list. Choosing an engineer outside the top of the list returns a warning (for example "different region" or "high workload"). The warning does not stop the allocation. [Q1]
  - Reality: the implemented warnings are "different region" and "no shift on that day". No workload warning is implemented.
- R9. Each allocation response includes a list of warnings. The list is empty when there is nothing to warn about.

**Booking and double-booking**
- R10. An engineer cannot have two bookings whose times overlap, unless the new booking is for a P1 work order. An overlapping non-P1 booking is refused with error 409. The check and the save happen in one transaction.
- R11. A P1 work order may be booked over an engineer's existing non-P1 booking that is not yet started. The existing work order is returned to Allocated with its booking cleared and is flagged as needing a rebook (R13). [Q2]
  - Reality: "not yet started" means the job has no actual start and is not OnSite. A P1 displacing a non-P1 job that is still Allocated or Scheduled with no visit started is allowed.
- R12. Until Q2 is answered, a P1 never displaces a work order that is OnSite, or another P1. Either case is refused with 409; the P1 is not booked, nothing is moved, and no warning is returned. [Q2]
- R13. A work order moved by a P1 is never deleted. Its status becomes Allocated and its booking is cleared. It is flagged as needing a rebook by the note written against it, which says which P1 moved it and when; there is no separate status. The dispatcher board lists flagged work orders in its "Needs rebooking" strip (R26). Whether the customer is told is decided under Q2. [Q2]
- R14. Next free slot: the system returns the earliest slot, within the next 14 days, where a suitable active engineer has a shift and no booking overlaps (P1 or not). The slot length is the job type's `DefaultMinutes`. The response gives the engineer id, the engineer name, the start and the end. If no slot exists, the response is error 404 (implemented as NotFoundException: "Free slot in the next 14 days for job {id} was not found."; the earlier draft said 422). The search starts from the `from` query date, or today (UTC) if none is given. [Q9]
- R15. Next free slot uses shift times only, less the shift's break. Travel time between jobs is not counted in version 1. [Q9, Q17]
  - Break timing: `Shift` stores only `BreakMinutes`, with no break start. The break is modelled as a block of `BreakMinutes` centred on the midpoint of the shift (`ShiftBreak.Overlaps` in `Services/ShiftBreak.cs`). A shift with `BreakMinutes` of 0 has no break.
  - A booking window must sit inside one shift and must not overlap that shift's break. Next-slot and schedule use the same helper, so a suggested slot is always accepted by schedule.
  - The day-capacity check (rostered hours less booked minutes) is unchanged.
- R16. When two slots start at the same time, the engineer ranked higher under R7 is chosen. [Q4]
  - Reality: the tie-break is same region first, then name, then engineer id. The R7 outward-code and workload ranks are not applied in next-slot. See the reconciliation notes in Delivery status.

**Visits, outcomes and parts**
- R17. Each visit records one outcome: Fixed, PartsNeeded, NoAccess or FollowUpRequired. [Q6]
- R18. An outcome other than Fixed leaves the work order open and moves it back to Triaged, so that a return visit can be booked. Whether a return visit is created automatically is decided under Q6. [Q6]
  - Reality: the follow-up visit is created automatically, unscheduled, with the next visit number. The job's scheduled and actual times are cleared, and the visit keeps its own times. Q6 is still open, so this default needs Daniel's confirmation.
- R19. Parts used on a visit are recorded against the work order. The stock reduction and the recording happen in one atomic step. If there is not enough stock, the request is refused with error 409 and nothing changes. Stock never goes below zero.
- R20. Stock is one figure per part in version 1. [Q10]

**First-time-fix**
- R21. A work order counts as fixed first time only if its first visit has the outcome Fixed and no later visit exists for it. An outcome of NoAccess counts as not fixed first time. [Q7]
  - In the code, `Job.FirstTimeFix` is set by the visit service on completion. `JobService.UpdateAsync` leaves it unchanged for any job that has visit rows; only legacy jobs with no visits keep the outcome-based flag.
  - Historical data (Q23): migration 007 created one visit (visit 1) for each historical Completed job with an outcome. That gives 207 visits. Legacy jobs never recorded return visits, so for them first-time-fix equals the old outcome flag: 152 of 207, or 73.4%. Only visits recorded from now on carry real return-visit data. The board will show these historical figures.
- R22. The monthly first-time-fix rate is: work orders fixed first time divided by work orders completed in the month. It is reported by region and by job type. Each work order is counted in the month of its first visit. [Q7]
  - Reality: the report counts visit 1 by its actual end time (the time its outcome was recorded), not its actual start. Cancelled jobs are excluded. The rate is 0 when there are no attended work orders, and is rounded to one decimal place.
- R23. The first-time-fix report is a new calculation. The monthly utilisation report is not changed.

**SLA and the dispatcher board**
- R24. The SLA due time is set from the priority when the work order is created: P1 4 hours, P2 24 hours, P3 72 hours. This matches the existing job rule.
- R25. On the board, an open work order is amber when less than 25% of its SLA window remains, and red when the SLA time has passed. [Q8]
  - Exact rule: amber only when the time remaining is strictly less than 25% of the window. A work order with exactly 25% of its window remaining is "ok", so exactly 75% used is "ok". Red (breached) once the current time is past the SLA due time. Finished work orders show "none". Implemented in `Services/SlaState.cs`.
- R26. The board shows engineers down the side and a seven-day view across the top. Each cell shows the work orders booked for that engineer and day. Work orders flagged for rebooking (R13) appear in their own "Needs rebooking" list at the top of the board.
  - Reality: `days` is 1 to 14, default 7. Unscheduled work orders (no booking) are returned in a separate list, and the flagged ones carry `needsRebook`.

**Parts reordering (manual version only, N1)**
- R27. When a part's stock is at or below its reorder level, the system creates one reorder request for that part. No second request is created while one is open. [Q14, Q20]
  - Reality: the check runs when a part is recorded against a job (the visit completion, or `POST /api/jobs/{id}/parts`), inside the same transaction as the stock decrement. "At or below" is implemented (Q20 asks whether it should be strictly below). A request in Ordered also blocks a new one; a Received request does not. Migration 008 backfilled one Open request for each part already at or below its level (DV-WB-01 and HP-CTRL at the time of writing). Stock edits through `PUT /api/parts/{id}` do not create a request (see the reconciliation notes).
- R28. A reorder request is marked "ordered" by a person, with an expected arrival date. The system does not contact any supplier. [Q14, Q21]
  - Reality: `POST /api/reorder-requests/{id}/ordered` takes `expectedOn` (a date, which may not be before today) and `userName`. It moves Open to Ordered and returns 409 if the request is not Open. The stored `orderedBy` is the user name (migration 008).
  - Receiving: `POST /api/reorder-requests/{id}/received` takes `quantity` and `userName`. It moves Ordered to Received, adds the quantity to the part's stock and stores `receivedBy`, in one transaction. It returns 409 if the request is not Ordered. This is not in the original brief; it is part of the manual version.
  - Statuses are the capitalised `Open`, `Ordered` and `Received`. No supplier is contacted.
- R29. When a work order's parts list includes a part with no stock and no expected arrival date, the booking shows a warning. The booking still goes ahead, unless Q14 decides that it must be blocked. [Q14, Q18]
  - Not built. Jobs and work orders have no required-parts data, so there is no parts list to check against a booking. Until Q18 is answered, this warning cannot exist.

## API

Paths are as built unless marked otherwise. Errors use the existing ProblemDetails format: 400 for validation, 404 for a missing record, 409 for a conflict (including an illegal status move, R2, and insufficient stock, R19), 422 for a business rule (skill, inactive engineer, outside a shift, and the other rules marked 422 below).

| Method and path | Purpose | Request | Success response | Errors |
|---|---|---|---|---|
| POST /api/work-orders | Not built. Work orders are created through `POST /api/jobs` and the CSV import (`POST /api/imports/job-requests`). | n/a | n/a | n/a |
| GET /api/work-orders | List work orders, paged | page, pageSize (1-100, default 25), status, regionId, engineerId, priority | 200 paged list, each row with SLA state | 400 |
| GET /api/work-orders/{id} | Read one work order | none | 200 with status, SLA state, engineer, notes and parts | 404 |
| POST /api/work-orders/{id}/status | Move to Triaged, or cancel. Only Requested to Triaged and any open status to Cancelled are accepted (R2). | status, userName | 200 with the work order | 400 (unknown status or missing userName), 404, 409 (move not allowed, R2 and R3) |
| GET /api/work-orders/{id}/candidates | Ranked suitable engineers (R7) | date (optional; defaults to the scheduled day, else today UTC) | 200 list with engineerId, engineerName, regionId, rank, rankReason, warnings | 404 |
| POST /api/work-orders/{id}/allocate | Allocate an engineer (R5 to R9) | engineerId, userName, date (optional) | 200 with jobId, status, engineerId, warnings (R9) | 404, 409 (status is not Requested or Triaged), 422 (no required skill R5, inactive R6) |
| POST /api/work-orders/{id}/schedule | Book a time with an engineer (R10 to R13) | engineerId (optional; defaults to the work order's engineer), start, userName. End is start + the job type's `DefaultMinutes`, so it is not sent. | 200 with jobId, status, engineerId, start, end and `displaced` (a list of work orders returned to Allocated, R11) | 404, 409 (overlap R10; a P1 over another P1 or an OnSite or started job, R12; status not Allocated or Scheduled; engineer changed while waiting for the lock), 422 (skill R5, inactive R6, window outside a shift or inside the break R15, past midnight, rostered hours exceeded) |
| GET /api/work-orders/{id}/next-slot | One call for the next free slot (R14 to R16) | from (optional; defaults to today UTC) | 200 with engineerId, engineerName, start, end | 404 (no slot in the 14 days from `from`, R14) |

| POST /api/work-orders/{id}/visits/start | Start a visit (job must be Scheduled; job moves to OnSite) | none | 200 with the visit | 404, 422 (status not Scheduled, or the visit has already started) |
| POST /api/work-orders/{id}/visits/complete | Record a visit outcome, with parts (job must be OnSite with a visit in progress) | outcome (Fixed, PartsNeeded, NoAccess or FollowUpRequired), notes (optional), userName, parts (partId, quantity; optional) | 200 with the visit | 400 (outcome not in the list, or a part quantity below 1), 404, 409 (stock R19), 422 (not OnSite, or no visit in progress) |
| GET /api/work-orders/{id}/visits | Visit history | none | 200 list | 404 |
| GET /api/dispatch/board | Board for a window of 1 to 14 days (default 7) | from (date, optional; defaults to today UTC), days (optional), regionId (optional) | 200 with from, days, engineers (each with cells per day: shift start and end, break minutes and booked jobs with SLA state), and unscheduled jobs with `needsRebook` | 400 |
| GET /api/reports/first-time-fix | First-time-fix rate (R21 to R23) | from, to (each a yyyy-MM month) | 200 with overall, byRegion and byJobType together; each row has name, jobsAttended, fixedFirstTime, ratePercent | 400 (bad month, or `to` before `from`) |
| GET /api/reorder-requests | List reorder requests (manual, N1) | status (Open, Ordered or Received), partId (optional), page, pageSize | 200 paged list | 400 (unknown status) |
| POST /api/reorder-requests/{id}/ordered | Mark as ordered (manual). The system does not contact a supplier. | expectedOn (a date, not before today), userName | 200 with the request | 400 (expectedOn in the past, or userName missing), 404, 409 (not Open) |
| POST /api/reorder-requests/{id}/received | Mark as received and add the quantity to stock, in one transaction | quantity (at least 1), userName | 200 with the request | 400, 404, 409 (not Ordered) |

Note on schedule 409s: a non-P1 overlap returns 409. A P1 over an existing booking is also refused with 409 when that booking is another P1 or is already on site (started, or status OnSite); the P1 is not booked and nothing is moved. A P1 may only displace other bookings that are neither P1 nor started. Who may be displaced is still open under Q2. The schedule call does not return a "no engineer free" warning (see Q16).

Concurrency (as built): the schedule transaction takes a row lock on the engineer (`SELECT ... FOR UPDATE`) and then on the job, before it reads the job's status or any bookings. A displaced job that cannot be loaded fails the whole booking with 409; it is never skipped. Allocate, visit start, visit complete and status change each take the job row lock as the first statement in their transaction, then read the job's status. Two concurrent allocations of one job therefore cannot both pass the status gate. Tests cover the lock order only; no test runs two requests concurrently (see AC-11 in the Traceability table).

## UI

**Work order list.** Table of work orders with status, priority, SLA state and engineer. Filters for status and region. Loading: a spinner in the table area. Empty: "No work orders match these filters." Error: a message with a retry button. Normal: rows, paged.

**Work order detail.** Shows the work order, its status and a status control that offers only allowed moves (R2). Panels: candidate engineers with warnings (R7, R8); a "Next free slot" button that shows the engineer and time from R14, with an "Accept" action; visits and outcomes, with a form to record parts used. Loading: skeleton panels. Empty visit history: "No visits yet." Error on allocation: the message from the API, shown next to the action. A warning from R9 is shown with a "Confirm anyway" button. A 422 skill block is shown as a blocking message, with no button to override it.

**Dispatcher board.** Engineers down the side, seven days across the top. Each cell lists work orders with an amber or red SLA marker (R25). A "Needs rebooking" strip of flagged work orders (R13) sits above the grid (R26). Loading: grey grid. Empty: "No engineers on shift in this window." Error: message with retry. Normal: populated grid.

**First-time-fix dashboard.** Rate by region and by job type, for a chosen month range, with counts beside each rate. Loading, empty ("No completed work orders in this range.") and error states as above.

**Reorder requests (manual).** List of requests with a status filter (Open, Ordered, Received, All). Each Open request has a "Mark ordered" action that asks for an expected date (R28). Each Ordered request has a "Mark received" action that asks for a quantity. Empty: "No open reorder requests." Error: message with retry.
- Reality: the list, the status filter, the two actions and the ordered-by and received-by columns are built in `frontend/src/pages/ReorderRequestsPage.tsx` and `frontend/src/components/reorder/ReorderActions.tsx`. The empty-state text is not checked against the code here.

**Work order detail, as built.** Status actions (Triage, Cancel) in `StatusActions.tsx`; candidate list in `AllocatePanel.tsx`; slot finder and schedule form in `SchedulePanel.tsx`; visit start and the outcome form with parts in `VisitPanel.tsx`. The "Confirm anyway" control and the "Accept" action from the spec are not confirmed in the code review and are not claimed as built.

## Acceptance Criteria

- **AC-01** Given a Raised work order for a boiler job, When a dispatcher asks for candidates, Then no engineer without the gas skill appears in the list, and the response does not offer an override. (R5)
- **AC-02** Given a boiler job and an engineer without the gas skill, When the dispatcher allocates that engineer, Then the request is refused with 422 and the work order is unchanged. (R5)
- **AC-03** Given an engineer in a different region, When the dispatcher allocates them to a job, Then the allocation succeeds and the response contains a warning. (R8, R9)
- **AC-04** Given an engineer already booked 09:00 to 11:00, When a non-P1 job is booked 10:00 to 12:00 with that engineer, Then the request is refused with 409 and no booking is saved. (R10)
- **AC-05** Given the same engineer and time, When a P1 job is booked 10:00 to 12:00, Then the booking is saved, the earlier job is returned to Allocated with no booking and flagged as needing a rebook, and a note records which P1 moved it. (R11, R13)
- **AC-06** Given an engineer whose current job is OnSite, When a P1 is booked over it, Then the request is refused with 409, nothing is booked or moved, and no warning is returned. (R12)
- **AC-07** Given a work order, When a dispatcher asks for the next free slot, Then one response gives the engineer and the start and end time. The slot uses shift time less the break block (`BreakMinutes` centred on the shift midpoint), does not overlap that break, and is the job's default length. Schedule accepts that slot. (R14, R15, Q17)
- **AC-08** Given no free slot in the next 14 days, When the next free slot is requested, Then the response is 404 (see R14). (R14)
- **AC-09** Given a work order in Completed status, When a status change to Cancelled is requested, Then the request is refused with 409. (R3)
- **AC-10** Given a part with 2 in stock, When a visit records 3 of that part, Then the request is refused with 409, the stock stays at 2 and no parts are recorded. (R19)
- **AC-11** Given two visits recorded at the same moment for the last unit of a part, When both are submitted, Then only one succeeds and stock never goes below zero. (R19)
- **AC-12** Given a visit with outcome PartsNeeded, When it is recorded, Then the work order is moved back to Triaged and is not counted as fixed first time. (R18, R21)
- **AC-13** Given a work order fixed on its second visit after a NoAccess first visit, When the monthly first-time-fix report is run, Then the work order is not counted as fixed first time. (R21, R22)
- **AC-14** Given an open work order with less than 25% of its SLA window remaining (more than 75% used), When the board is shown, Then the work order is marked amber. A work order with exactly 75% used (exactly 25% remaining) is "ok", not amber. (R25)
- **AC-15** Given a part whose stock falls to its reorder level, When the stock change is saved, Then one reorder request is created, and a second change while it is open creates no second request. (R27)
- **AC-16** Given an open reorder request, When a user marks it ordered with an expected date, Then the request shows the date and no supplier is contacted. (R28)
- **AC-17** Given the monthly utilisation report, When this work is deployed, Then its output is unchanged. (R23)

## Assumptions & Open Questions

| # | Assumption or question | Owner | Status |
|---|---|---|---|
| Q1 | (C1) Is the gas and required-skill rule absolute, with no override by anyone? Which other checks may only warn? | Daniel Mensah | Open |
| Q2 | (C2) When a job is moved by a P1, who is told (dispatcher only, or the customer too)? Can a P1 move another P1? Can an OnSite job be interrupted? | Daniel Mensah | Open |
| Q3 | (G1) Are the status names right? Who may move a work order between states? | Daniel Mensah | Open |
| Q4 | (G2) Is the proxy ranking (region, postcode outward code, fewest hours) acceptable for version 1? If true distance is wanted, which geocoding provider, at what cost? | Daniel Mensah (decision); Delivery team (cost) | Open |
| Q5 | (G3) Is the job type to skill mapping in the seed data correct? | Delivery team | Open |
| Q6 | (G4) Is the outcome mapping (R17, R18) correct? Is a return visit created automatically? | Daniel Mensah | Open |
| Q7 | (G5) Is "no access" a failed first visit? Is a follow-up within a set number of days a return visit? Is the month set by the first visit? | Daniel Mensah | Open |
| Q8 | (G6) Is amber at 25% of the window left, and red when breached, correct? | Daniel Mensah | Open |
| Q9 | (G7) Is the next-slot search 14 days, shift time less breaks, job length from the job type, with no travel time? | Daniel Mensah | Open |
| Q10 | (G8) Do we need separate van and stores stock? If so, who counts stock? | Daniel Mensah | Open |
| Q11 | (G9) Are there about ten engineers, each covering one region only? | Delivery team | Open |
| Q12 | (G10) Are the roles dispatcher, engineer and field operations manager, and what may each do? | Daniel Mensah | Open |
| Q13 | (G10, auth) Is a named user supplied with each action (for example a header) acceptable as the minimum until login exists? | Delivery team | Open |
| Q14 | (N1) Supplier name and whether it has an API. Who approves spend? Is a warning enough, or must booking be blocked? | Daniel Mensah | Open |
| Q15 | (N2) Drop call recording, or commission the compliant version? Data Protection Officer sign-off. | Daniel Mensah | Open |
| Q16 | (R12) When a P1 is booked and no other engineer is free to take the displaced work, should the schedule response carry a "no engineer free" warning (R12 says it does)? The current implementation returns no such warning. Is it needed, or is the rebook flag and note enough? | Delivery team | Open |
| Q17 | (R15) Is a break modelled as `BreakMinutes` centred on the shift midpoint acceptable? The schema has no break start time. If Daniel needs real break times, a new column is needed and the helper changes with it. Please confirm break timing. | Daniel Mensah | Open |
| Q18 | (R29, parts) A work order cannot yet say which parts it needs, so the "part not coming" booking warning cannot be built. How should a work order state its parts: the engineer or dispatcher records expected parts on the work order, or there is a standard parts list per job type? Until answered, R29 stays Not built. | Daniel Mensah | Open |
| Q19 | (R12, R18, visits) If a work order is On site and is cancelled, the visit is left open. Should cancellation be allowed (and the open visit closed as cancelled), or should a completed outcome be required first? | Daniel Mensah | Open |
| Q20 | (R27) Should the reorder trigger be "at or below the reorder level" (as built) or strictly "below" it? | Daniel Mensah | Open |
| Q21 | (N1, R28) Who approves spend on a reorder? Ordering is a financial commitment, and pricing and invoicing are out of scope. Who is the supplier, and does the manual "ordered" step need a second approver? | Daniel Mensah | Open |
| Q22 | (G10, R4) Who may do each action: change status, allocate, schedule, start and complete visits, mark reorders ordered and received, and cancel? No authentication exists, so the "acting as" name is recorded but not verified. Until login exists, is a recorded name enough, or does this need a stronger control? | Daniel Mensah | Open |
| Q23 | (R21, R22, acknowledgement) Historical first-time-fix: migration 007 created one visit for each of the 207 historical completed jobs, so the historical rate equals the old outcome flag, 152 of 207 (73.4%). Only new work records real return-visit data. Daniel must acknowledge that the board will show these historical figures before they are published. | Daniel Mensah | Open |
| A1 | Version 1 uses one stock figure per part and one region per engineer. | Delivery team | Open |
| A2 | Work order ids and job ids are the same records for version 1; the existing `Job` table is extended rather than replaced. | Delivery team | Open |
| A3 | Migration numbering continues from the existing scripts; the next file is `006`. | Delivery team | Open |
| A4 | Timestamps for scheduling use the same clock as the existing job code (UTC for jobs). The shift service uses local time; this is to be decided before build. | Delivery team | Open |

## Traceability

Test names below are real method names in `backend/Corrin.Tests` (grep of `[Fact]` and `[Theory]` methods). Where a requirement has no test, or the named behaviour is not built, the cell says so. No test name has been invented. Class names are omitted; the method name is unique enough to find.

| Requirement in the brief | Rule | AC | Endpoint or screen | Test name |
|---|---|---|---|---|
| Work order with named steps (raised to finished) and status moves | R1, R2, R3, R4 | AC-09 | POST /api/work-orders/{id}/status; work order detail | ChangeStatus_CompletedJob_CancelIsRefusedWithConflict; ChangeStatus_WholeMatrix_AllowedMovesSaveAndEveryOtherMoveConflicts; CanMoveByStatusEndpoint_WholeMatrix_MatchesSpecR2AndR3; CanMoveByStatusEndpoint_UnknownTargetStatus_IsFalse; Cancel_CompletedJob_ThrowsConflict; IsFinal_FinalStatuses_AreTrue; IsFinal_OpenStatuses_AreFalse; Validate_EveryKnownStatus_IsAccepted; Validate_UnknownStatus_IsRejected; ChangeStatus_RequestedToTriaged_WritesNoteWithAuthorAndUserName; ChangeStatus_Cancel_RecordsThePreviousStatusInTheNote; ChangeStatus_RequestedJobToStatusOnlyReachedByOtherEndpoints_Returns409AndJobUnchanged (API; refusal of Requested to Allocated via the status endpoint, matched by name). |
| Status endpoint input checks, 404 and locking | R2, R3, R4 | AC-09 | POST /api/work-orders/{id}/status | ChangeStatus_UnknownStatus_Returns400AndJobUnchanged; ChangeStatus_MissingUserName_Returns400; Validate_MissingUserName_IsRejected; Validate_UserNameLongerThan80_IsRejected; ChangeStatus_UnknownJob_ThrowsNotFoundAndWritesNothing; ChangeStatus_LocksJobBeforeReadingStatus; ChangeStatus_StatusChangedWhileWaitingForLock_ConflictsAndWritesNothing; ChangeStatus_CompletedJobToAnyStatus_Returns409AndJobUnchanged (API). |
| Work order list and detail (no numbered rule in the spec) | None | None | GET /api/work-orders; GET /api/work-orders/{id}; work order list | List_NoFilters_Returns200WithPagedShapeAndSlaStateOnEveryItem; List_StatusFilter_NarrowsResultsToThatStatus; List_PriorityFilter_NarrowsResultsToP1; List_RegionFilter_NarrowsResultsToRegion1; List_CombinedStatusAndRegion_NarrowsToJob235Only; List_SecondPage_ReturnsADifferentSetOfRows; List_PageSizeAboveMax_IsClampedTo100; List_PageSizeZero_IsClampedToOne; List_PageZero_IsClampedToOne; List_InvalidStatusOrPriority_Returns400; Get_SeededJob235_Returns200WithNotesPartsAndNames; Get_SeededCompletedJob_HasSlaStateNoneAndOutcome; Get_UnknownId_Returns404ProblemDetails; ListAsync_PageSize_IsClampedToOneThroughOneHundred; ListAsync_Page_IsAtLeastOne; ListAsync_Filters_ArePassedToTheRepositoryUnchanged; ListAsync_NoRows_ReturnsEmptyPageWithZeroTotal; ListAsync_TotalComesFromRepositoryNotFromPageLength; ListAsync_MapsNotesAndPartsOnlyToTheirOwnJob_AndComputesSlaPerRow; ListAsync_RowWithNoAsset_HasNullAssetSummary; GetAsync_UnknownId_ThrowsNotFound; GetAsync_KnownId_ReturnsRowWithNotesPartsAndAmberSla; GetAsync_NoNotesOrParts_ReturnsEmptyArrays; Validate_EveryKnownStatusFilter_IsAccepted; Validate_UnknownStatusFilter_IsRejected; Validate_KnownPriorityFilter_IsAccepted; Validate_UnknownPriorityFilter_IsRejected; Validate_NoFilters_IsAccepted; Validate_EmptyStatusAndPriority_MeansNoFilter_AndIsAccepted; Validate_ZeroRegionId_IsRejected. |
| Boiler job never to non-gas engineer (hard skill block, nobody can override) | R5, R6 | AC-01, AC-02 | GET /candidates; POST /allocate; POST /schedule | Candidates_SeededGasJob_Returns200WithRankedGasEngineers; GetCandidates_QueriesOnlyActiveEngineersWithRequiredSkill; Allocate_BoilerGasJobToEngineerWithoutGasSkill_Returns422AndJobUnchanged; Allocate_EngineerWithoutRequiredSkill_ThrowsBusinessRuleAndLeavesJobUnchanged; Allocate_InactiveEngineer_ThrowsBusinessRuleAndLeavesJobUnchanged; Schedule_EngineerWithoutRequiredSkill_ThrowsBusinessRule; Schedule_InactiveEngineer_ThrowsBusinessRule; EngineerWithoutSkillOrInactive_Is422AndNothingIsSaved (EngineerAssignmentSafetyTests); ActiveEngineerWithRequiredSkill_IsAccepted (EngineerAssignmentSafetyTests). AC-01 "the response does not offer an override": No test. |
| Ranked candidate list (same region, outward code, fewer hours) | R7 | AC-03 (lists engineers) | GET /candidates | GetCandidates_RanksSameRegionFirstThenFewerJobs; GetCandidates_SameOutwardCodeRanksAheadWithinRegion; GetCandidates_NoEngineersInPool_ReturnsEmptyList; GetCandidates_UnknownJob_ThrowsNotFound; Candidates_UnknownJob_Returns404ProblemDetails (API). The name tie-break and the full ranking are not separately tested. |
| Dispatcher can put any engineer on any job, with warnings | R8, R9 | AC-03 | POST /allocate | Allocate_DifferentRegionAndNoShift_SucceedsWithBothWarnings; Allocate_SameRegionWithShift_ReturnsNoWarnings. Workload warning (R8): No test, not built. |
| Allocation status gate (Requested or Triaged; closed or advanced refused) | R2 (see reconciliation notes: Requested is accepted) | None | POST /allocate | Allocate_OpenStatus_Succeeds; Allocate_ClosedOrAdvancedStatus_ThrowsConflictAndLeavesJobUnchanged; Allocate_Success_WritesAllocationNoteWithUserName; Allocate_LocksJobBeforeReadingStatus; Allocate_StatusChangedWhileWaitingForLock_ThrowsConflictAndWritesNothing; Allocate_UnknownJob_ThrowsNotFound; Allocate_UnknownEngineer_ThrowsNotFound; Allocate_UnknownJob_Returns404 (API). |
| Never double-booked (non-P1 overlap refused) | R10 | AC-04 | POST /schedule | Schedule_NonP1OverlappingBooking_ThrowsConflictAndSavesNothing; Schedule_BackToBackAfterExistingBooking_Succeeds; Schedule_BackToBackBeforeExistingBooking_Succeeds; Schedule_JobNotAllocatedOrScheduled_ThrowsConflict; Schedule_EngineerRowLockTakenBeforeBookingsAreRead; Schedule_LocksEngineerThenJobBeforeReadingJobStatus; Schedule_StatusChangedWhileWaitingForLock_ThrowsConflictAndWritesNothing; Schedule_EngineerReassignedWhileWaitingForLock_ThrowsConflictAndWritesNothing; Schedule_NoEngineerSuppliedAndNoneOnJob_ThrowsBusinessRule; Schedule_UnknownJob_ThrowsNotFound. |
| Schedule window inside a shift, not across midnight, within rostered hours | R15 (shift part) | AC-07 (schedule accepts a slot) | POST /schedule | Schedule_WindowOutsideShift_ThrowsBusinessRuleAndSavesNothing; Schedule_WindowEndingExactlyAtShiftEnd_Succeeds; Schedule_WindowCrossingMidnight_ThrowsBusinessRule; Schedule_ExceedsRosteredHoursForDay_ThrowsBusinessRule; Schedule_WindowOverlappingBreak_ThrowsBusinessRuleAndSavesNothing; Schedule_WindowAdjacentToBreak_Succeeds. |
| P1 always goes on straight away (displaces non-P1, not started) | R11, R12, R13 | AC-05, AC-06 | POST /schedule; dispatcher board | Schedule_P1OverlappingP3_MovesP3AndReturnsItAsDisplaced (asserts the "Displaced by P1 ... UTC by ..." note, so the AC-05 note is covered); Schedule_P1OverOnSiteJob_ThrowsConflictAndDoesNotMoveIt; Schedule_P1OverStartedJob_ThrowsConflictAndDoesNotMoveIt; Schedule_P1OverP1_ThrowsConflictAndMovesNothing; DisplacedJobIds_ReturnsOnlyJobsWhoseNoteStartsWithThePrefix. AC-06 "no warning is returned": No test. "A displaced work order is never deleted" (R13): No test. |
| P1 displacement re-check under locks (job changed after the overlap read) | R11, R12 (Delivery status: concurrency) | AC-05, AC-06 | POST /schedule | P1_DisplacedJobStartedAfterOverlapRead_Throws409AndSavesNothing; P1_DisplacedJobOnSiteAfterOverlapRead_Throws409AndSavesNothing; P1_DisplacedJobBecameP1AfterOverlapRead_Throws409AndSavesNothing; P1_DisplacedJobFinalisedAfterOverlapRead_Throws409AndSavesNothing; P1_DisplacedJobMovedAfterOverlapRead_Throws409AndSavesNothing; P1_DisplacedJobRemovedAfterOverlapRead_Throws409AndSavesNothing; P1_SecondDisplacedJobFailsRecheck_ThrowsAndNeverSavesTheP1; P1_LocksEngineerThenP1ThenDisplacedJobsInAscendingIdThenRereads; Schedule_P1DisplacedBookingCannotBeLoaded_ThrowsConflictAndSavesNothing. These check lock order and the re-check with mocks only; no test runs two requests concurrently. |
| P1 full-day one-for-one swap (Delivery status, Built) | R11, R15 (capacity) | None | POST /schedule | P1_SwapOnAFullDay_SucceedsAndDisplacesBothBookings; NonP1_OnAFullDay_Throws422RatherThanDisplacing; P1_OverAnotherP1OnAFullDay_Throws409NotCapacity. |
| No engineer free warning on a P1 (Q16) | R12 (Q16) | None | POST /schedule | Not built. No test. |
| One call gives next slot and engineer | R14, R15 | AC-07, AC-08 | GET /next-slot | NextSlot_ReturnsEarliestSlotAcrossEngineers; NextSlot_RespectsExistingBookings; NextSlot_EngineerWhoseRosteredHoursAreFullIsSkipped; NextSlot_FirstFreeGridSlotFallsInBreak_OffersSlotAfterBreak; NextSlot_WindowEndingExactlyAtBreakStart_IsOffered; NextSlot_UnknownJob_ThrowsNotFound; NextSlot_EveryShiftFullyBooked_ThrowsNotFound; NextSlot_NoShiftInWindow_ThrowsNotFound; NextSlot_NoEngineerWithRequiredSkill_ThrowsNotFound. AC-08 maps to the three ThrowsNotFound tests. No API test exists for GET /next-slot. |
| Next slot never starts before UK now, and uses BST correctly | R14 (Delivery status: UK wall-clock) | AC-07 | GET /next-slot | NextSlot_BstMorning_NeverOffersAStartBeforeUkNow; NextSlot_BstMorningOnTheGrid_OffersThatExactSlot. |
| Next slot tie-break | R16 | None | GET /next-slot | NextSlot_SameStartTime_PrefersSameRegionEngineer (covers the region rule only; the full R7 ranking is not applied, see Delivery status). |
| Break block helper (BreakMinutes centred on the shift midpoint) | R15 | AC-07 | GET /next-slot; POST /schedule | Overlaps_WindowInsideOrCrossingBreak_ReturnsTrue; Overlaps_WindowClearOrTouchingBreakEdge_ReturnsFalse; Overlaps_NoBreakMinutes_ReturnsFalseForAnyWindow; Overlaps_OddBreakMinutes_HalfMinuteEdgesAreExact; Overlaps_WindowOnDifferentDayFromShift_UsesDayBase (ShiftBreakTests). |
| UK wall-clock: BST and GMT conversion, past starts on schedule (422, P1 15 minute grace) | Delivery status (Times); API table (schedule 422) | None | POST /schedule; GET /next-slot | FromUtc_InBritishSummerTime_IsOneHourAheadOfUtc; FromUtc_InGreenwichTime_IsTheSameAsUtc; FromUtc_AtTheOctoberChangeOver_FallsBackToGmt; Now_WithOverride_ReturnsTheOverriddenUkTime; Schedule_NonP1StartInThePast_Throws422AndSavesNothing; Schedule_BstStartBetweenUtcAndUkNow_IsRejected; Schedule_NonP1StartExactlyAtUkNow_IsAccepted; Schedule_P1ExactlyFifteenMinutesInThePast_IsAccepted; Schedule_P1MoreThanFifteenMinutesInThePast_Throws422AndSavesNothing (all UkClockTests). |
| Visit outcomes recorded (Fixed, PartsNeeded, NoAccess, FollowUpRequired) | R17, R18 | AC-12 | POST /visits/complete | CompleteAsync_NotFixedOutcome_ReopensJobAndCreatesUnscheduledFollowUpVisit (InlineData PartsNeeded, NoAccess, FollowUpRequired); CompleteAsync_NotFixed_ClearsJobActualTimesButVisitKeepsItsOwn; CompleteAsync_Fixed_KeepsJobActualStartAndSetsActualEnd; CompleteAsync_FixedWithNotes_NoteGetsFullStop; CompleteAsync_NotFixedNotes_TrimmedAndFullStopAddedOnlyWhenMissingPunctuation; CompleteAsync_OutcomeNotInVocabulary_ThrowsBusinessRuleBeforeReadingJob; CompleteVisit_OutcomeNotInVocabulary_Returns400 (API); Validate_AllowedOutcome_IsValid; Validate_OutcomeNotInVocabulary_IsRejected. AC-12 "not counted as fixed first time": see the First-time-fix rows. |
| Visit start (job must be Scheduled, moves to OnSite) | Delivery status (Visits) | None | POST /visits/start | StartAsync_ScheduledJobWithNoVisits_CreatesVisitOneAndSetsJobOnSite; StartAsync_FollowUpVisitAlreadyBooked_ReusesItAndCreatesNoNewVisit; StartAsync_JobNotScheduled_ThrowsBusinessRuleAndChangesNothing; StartAsync_LatestVisitAlreadyStarted_ThrowsBusinessRuleAndChangesNothing; StartAsync_UnknownJob_ThrowsNotFound; StartAsync_LocksJobBeforeReadingStatus; StartAsync_StatusChangedWhileWaitingForLock_ThrowsBusinessRuleAndChangesNothing; StartVisit_UnknownJob_Returns404; StartVisit_SeededRequestedJob_Returns422AndJobUnchanged (API). |
| Visit completion refused when not on site, or no visit in progress | Delivery status (Visits) | None | POST /visits/complete | CompleteAsync_JobNotOnSite_ThrowsBusinessRuleAndChangesNothing; CompleteAsync_NoVisitRecorded_ThrowsBusinessRuleAndChangesNothing; CompleteAsync_VisitNotStarted_ThrowsBusinessRuleAndChangesNothing; CompleteAsync_VisitAlreadyEnded_ThrowsBusinessRuleAndChangesNothing; CompleteAsync_UnknownJob_ThrowsNotFound; CompleteAsync_LocksJobBeforeReadingStatus; CompleteAsync_StatusChangedWhileWaitingForLock_ThrowsBusinessRuleAndChangesNothing; CompleteVisit_SeededRequestedJobWithNoVisit_Returns422; CompleteVisit_UnknownJob_Returns404 (API). |
| Visit history | Delivery status (Visits) | None | GET /api/work-orders/{id}/visits | ListAsync_UnknownJob_ThrowsNotFound; ListAsync_JobWithNoVisits_ReturnsEmptyList; ListAsync_TwoVisits_ReturnsThemInVisitOrderWithTheirOutcomes; Visits_UnknownJob_Returns404ProblemDetails; Visits_SeededJobWithNoVisitRows_ReturnsEmptyList (API). The "No visits yet" text on screen: No test. |
| Parts used, stock right afterwards, never below zero | R19, R20 | AC-10, AC-11 | POST /visits/complete; POST /api/jobs/{id}/parts | AddPart_InsufficientStock_ThrowsConflict; AddPart_ZeroRowsAffected_ThrowsConflictWithStockMessageAndSkipsJobPart; AddPart_ZeroRowsAffected_MessageUsesReReadStockNotStaleRead; AddPart_ZeroRowsAffectedAndPartNoLongerReadable_SaysOnlyZero; AddPart_ZeroStockOnHand_ThrowsConflictSayingZeroInStock; AddPart_Success_DecrementsStockAndAddsJobPart; AddPart_LocksJobBeforeReadingStatusAndDecrementingStock; AddPart_CompletedJob_IsAllowed; CompleteAsync_InsufficientStock_ThrowsConflictAndLeavesVisitOpen; CompleteAsync_WithParts_RecordsEachPartBeforeVisitAndJobAreSaved; CompleteAsync_NoParts_DoesNotCallAddPart; CompleteVisit_PartQuantityZero_Returns400 (API); TryDecrementStock_QuantityEqualsStock_DecrementsToZero; Validate_PartQuantityAtBoundary_IsValid; Validate_PartQuantityOutsideOneToFifty_IsRejected; Validate_SecondPartWithZeroQuantity_IsRejectedWithItsIndex. AC-10 (refused, stock stays at 2) is covered by AddPart_InsufficientStock_ThrowsConflict and CompleteAsync_InsufficientStock_ThrowsConflictAndLeavesVisitOpen. AC-11 (two concurrent visits, last unit): No test. The conditional decrement is tested single-threaded only; the lock-order tests above are the only concurrency coverage. |
| Stock edit through PUT /api/parts/{id} does not create a reorder request | Reconciliation notes (R27, AC-15) | None | PUT /api/parts/{id} | No test. |
| First-time-fix rule (first visit Fixed and no later visit) | R21 | AC-13 | GET /api/reports/first-time-fix; visit completion | CompleteAsync_FixedOnVisitOne_CompletesJobAndMarksFirstTimeFix; CompleteAsync_FixedOnVisitTwoAfterNoAccess_CompletesJobButIsNotFirstTimeFix; GetCohortCountsAsync_JobFirstTimeFixFlagDisagreesWithVisits_VisitHistoryDecides; UpdateAsync_NoVisits_FirstTimeFixFollowsOutcome; UpdateAsync_WithVisits_FirstTimeFixUnchangedWhateverTheOutcome; Update_LegacyJobWithNoVisits_FirstTimeFixFollowsOutcome. |
| First-time-fix monthly rate by region and job type (denominator, rounding, cohort) | R21, R22 | AC-13 | GET /api/reports/first-time-fix; FTF dashboard | GetCohortCountsAsync_ScenarioRows_CountsFirstVisitsInRangeNotCancelledAndFixedOnlyWithoutLaterVisit; GetCohortCountsAsync_NoVisitsInRange_ReturnsNoRows; GetAsync_NoCompletedWorkOrders_ReturnsZeroRateAndEmptyGroups; GetAsync_RateIsFixedOverAttendedRoundedToOneDecimal; GetAsync_AttendedButNotFixedFirstTime_CountsInDenominatorOnly; GetAsync_MultipleRows_OverallEqualsSumOfRowsAndGroupsAreOrderedByName; GetAsync_MonthRange_PassesFirstDayOfFromToFirstDayAfterTo; Validate_WellFormedRange_IsValid; Validate_BadMonthFormat_IsRejected; Validate_ToBeforeFrom_IsRejectedOnTo; Validate_ToBeforeFromInSameYear_IsRejected; FirstTimeFix_ValidRange_Returns200WithReportShapeAndRatesInRange; FirstTimeFix_InvalidQuery_Returns400 (API). |
| First-time-fix report on the backfilled history (207 attended, 152 fixed, 73.4%) | R21, R22, Q23 | None (Q23 acknowledgement is open) | GET /api/reports/first-time-fix | FirstTimeFix_WideRangeWithBackfilledVisits_OverallIs207Attended152Fixed73Point4Percent; FirstTimeFix_WideRangeWithBackfilledVisits_RegionAndJobTypeRowsSumToOverall (API). |
| First-time-fix is a separate calculation; utilisation report unchanged | R23, AC-17 | AC-17 | GET /api/reports/first-time-fix; GET /api/reports/utilisation | No test specific to this work. Existing utilisation tests: Utilisation_ReturnsWorkedHoursAsPercentage; Utilisation_ReturnsOneRowPerActiveEngineer; Utilisation_PartTimersAndFullTimers_UseRosteredHours; Utilisation_DeductsBreakMinutesFromAvailableHours; Utilisation_PartTimerIsNotPenalised; Utilisation_EngineerWithNoJobsIsZero; Utilisation_JobsWithoutShifts_ReturnsZeroWithoutException; Utilisation_RejectsReversedDates. These show the report still behaves as before; they do not compare against a pre-change output. |
| Visit backfill migration 007 (one visit per historical completed job) | Migration 007 (Q23) | None | Migration at API startup | BackfillVisitsMigration_RerunOnAlreadyBackfilledTable_InsertsZeroRows; BackfillVisitsMigration_RerunAfterVisitsRemoved_RestoresOneVisitPerCompletedJob; WorkOrderVisits_BackfilledCompletedJob1_ReturnsOneFixedVisitNo1 (API). |
| SLA due time set at creation (P1 4 hours, P2 24 hours, P3 72 hours) | R24 | None | POST /api/jobs; CSV import | No test asserts the due times set at creation. |
| Dispatcher board: engineers down the side, days across the top | R26 | None | GET /api/dispatch/board | GetBoardAsync_DefaultsToSevenCellsFromTheGivenDate; GetBoardAsync_Days_IsClampedToOneThroughFourteen; GetBoardAsync_NoEngineers_ReturnsNoRowsAndSkipsShiftAndBookingQueries; GetBoardAsync_EmptyCell_HasNoShiftTimesZeroBreakAndNoJobs; GetBoardAsync_TwoShiftsOnOneDay_ShowsEarliestStartLatestEndAndSummedBreaks; GetBoardAsync_BookedJob_AppearsOnlyInItsEngineerAndStartDayCell; GetBoardAsync_BookingWindowIsFromStartOfFirstDayToStartOfDayAfterLast; GetBoardAsync_RegionFilter_IsPassedToEngineersAndUnscheduled; Board_January2026Window_Returns200WithSevenCellsPerEngineerAndUnscheduled; Board_SeptemberWindow_ShowsShiftsAndBookedJobInTheRightCell; Board_DaysZero_IsClampedToOneDay; Board_DaysNinetyNine_IsClampedToFourteenDays; Board_RegionFilter_NarrowsEngineersAndUnscheduled. The empty-state text on screen: No test. |
| Dispatcher board with SLA flags (amber under 25% left, red when breached) | R25, R24 (creation) | AC-14 | GET /api/dispatch/board; GET /api/work-orders | Compute_JustUnderQuarterRemaining_ReturnsAmber; Compute_OpenStatusInsideAmberWindow_ReturnsAmber; Compute_ExactlyThreeQuartersUsed_IsNotAmber_PerLessThanRule; Compute_ExactlyQuarterRemaining_ReturnsOk; Compute_JustOverQuarterRemaining_ReturnsOk; Compute_JustCreated_ReturnsOk; Compute_OpenStatusPastDue_ReturnsBreached; Compute_NowExactlyAtDue_IsNotBreached; Compute_NullSlaDueAt_ReturnsNone; Compute_FinalStatusPastDue_ReturnsNone; Compute_FinalStatusInsideWindow_ReturnsNone; GetAsync_KnownId_ReturnsRowWithAmberSla; GetBoardAsync_SlaStateOnCellsAndUnscheduled_FollowsTheSlaRule. |
| Needs rebooking flag and strip (displaced, unstarted, allocated) | R13, R26 | AC-05 (board part) | GET /api/dispatch/board | GetBoardAsync_NeedsRebook_FlagsOnlyUnstartedAllocatedJobsThatAPrecedingP1Displaced; GetBoardAsync_NoDisplacedJobs_NothingFlagged; Board_DisplacedAllocatedWithNoTime_IsFlaggedNeedsRebook_AndScheduledWithSameNoteIsNot; Board_CancelledBookingIsNotShown_AndTriagedWithNoTimeIsUnscheduledWithoutFlag; Unscheduled_IncludesRequestedTriagedAndAllocatedWithoutTime_ExcludesScheduledAndCancelled. The "Needs rebooking" strip on screen: No test. |
| Board unscheduled list: cap of 200, order, and unscheduledTotal | Delivery status (Built, board) | None | GET /api/dispatch/board | GetBoardAsync_UnscheduledOverTheCap_IsCappedAtTwoHundredAndTotalIsUncapped; GetBoardAsync_UnscheduledExactlyAtTheCap_IsNotTruncated; GetBoardAsync_NoUnscheduledJobs_ReturnsEmptyListAndZeroTotal; GetBoardAsync_Unscheduled_OrdersRebookThenPriorityThenSlaThenId; GetBoardAsync_CapFullOfP1_KeepsFlaggedRowAndDropsTheLastP1. The "Showing N of TOTAL" text on screen: No test. |
| Reorder request created when stock is at or below the level (one open or ordered at a time) | R27 | AC-15 | POST /api/jobs/{id}/parts; visit completion | AddPart_CrossesReorderLevel_CreatesOneOpenRequestAndSecondDecrementAddsNone; AddPart_Success_ReorderCheckRunsAfterDecrementInsideTransaction; AddPart_ReorderCheckFails_ThrowsAndDoesNotSaveJobPart; AddPart_InsufficientStock_DoesNotCallReorderCheck; EnsureReorder_AtOrBelowLevelWithNoOpenRequest_CreatesOneOpenRequest; EnsureReorder_ExactlyAtLevel_CreatesOneOpenRequest; EnsureReorder_OneAboveLevel_CreatesNoRequest; EnsureReorder_AboveLevel_CreatesNoRequest; EnsureReorder_CalledTwiceAtLevel_CreatesOnlyOneOpenRequest; EnsureReorder_OpenRequestExists_CreatesNoDuplicate; EnsureReorder_OrderedRequestExists_CreatesNoDuplicate; EnsureReorder_OnlyReceivedRequestExists_CreatesOpenRequest; EnsureReorder_ReceivedRequestExists_StillCreatesOneOpenRequest; EnsureReorder_UnknownPart_CreatesNoRequest; HasOpenOrOrdered_OnlyOpenAndOrderedBlock; HasOpenOrOrdered_NoRequests_ReturnsFalse. AC-15 "when the stock change is saved" via PUT /api/parts: No test (see the stock edit row). |
| Reorder transitions: marked ordered with an expected date, then received into stock | R28 | AC-16 | POST /api/reorder-requests/{id}/ordered; POST .../received | MarkOrdered_Open_MovesToOrderedWithDateAndUser; MarkOrdered_OpenRequest_SetsOrderedFieldsAndReadBackReturnsJoinedRow; MarkOrdered_UnknownId_ThrowsNotFound; MarkOrdered_NotOpen_ThrowsConflictAndDoesNotChangeRow; MarkOrdered_LostRace_SecondCallerGetsConflict; MarkOrdered_PastExpectedOn_Returns400AndLeavesRequestOpen; MarkOrdered_MissingUserName_Returns400AndLeavesRequestOpen (API); MarkOrdered_UnknownId_Returns404 (API); TryMarkOrdered_SecondCallOnSameRow_ReturnsZeroAndKeepsFirstCaller; TryMarkOrdered_RequestNotOpen_ReturnsZeroAndChangesNothing; MarkReceived_Ordered_IncrementsStockInsideTransactionAndRecordsUser; MarkReceived_OrderedRequest_SetsReceivedByAndIncrementsStockByExactlyQuantity; MarkReceived_UnknownId_ThrowsNotFoundAndDoesNotTouchStock; MarkReceived_NotOrdered_ThrowsConflictAndAddsNoStock; MarkReceived_LostRace_ConflictAndNoStockAdded; MarkReceived_OpenRequest_Returns409AndLeavesRequestAndStockUnchanged (API); MarkReceived_UnknownId_Returns404 (API); TryMarkReceived_OpenRequest_ReturnsZeroAndStaysOpen; TryMarkReceived_SecondCallOnSameRow_ReturnsZeroAndKeepsFirstReceiver; IncrementStock_AddsExactlyTheQuantity; IncrementStock_UnknownPart_ReturnsZero; Ordered_ExpectedOnYesterday_IsRejectedOnExpectedOn; Ordered_ExpectedOnToday_IsAccepted; Ordered_UserNameEmpty_IsRejectedOnUserName; Received_QuantityZeroOrNegative_IsRejectedOnQuantity; Received_QuantityAboveUpperBound1001_IsRejectedOnQuantity. AC-16 "no supplier is contacted": No test (there is no supplier integration to call). |
| Reorder list (manual version of N1) | R28 | None | GET /api/reorder-requests | ListAsync_PageSize_IsClampedToOneToOneHundred; ListAsync_PageBelowOne_IsTreatedAsPageOne; ListAsync_PassesStatusAndPartFilterThroughAndMapsRows; ListAsync_NoRows_ReturnsEmptyPage; ListAsync_JoinsPartFieldsAndFlagsBelowReorderLevelAtTheBoundary; ListAsync_StatusFilter_ExcludesOtherStatuses; ListAsync_NoMatchingRows_ReturnsZeroTotalAndNoItems; List_Returns200WithTheTwoBackfilledOpenRequests; List_StatusOpen_ReturnsOnlyOpenRequests; List_PartIdTwo_ReturnsOnlyPartTwo; List_UnknownStatus_Returns400; ListQuery_KnownStatus_IsAccepted; ListQuery_UnknownStatus_IsRejectedOnStatus; ListQuery_PartIdZeroOrNegative_IsRejectedOnPartId; ListQuery_NoPartId_IsAccepted. |
| Reorder backfill migration 008 (Open request for each part at or below its level) | Migration 008 (R27) | None | Migration at API startup | BackfillReorderMigration_PartAtLevelWithNoOpenOrOrdered_InsertsOneOpenRow; BackfillReorderMigration_RerunOnAlreadyBackfilledData_InsertsZeroRows; List_Returns200WithTheTwoBackfilledOpenRequests (API). |
| Legacy POST and PUT /api/jobs apply the hard skill and inactive rule (422, no override) | Reconciliation notes (same rule as R5 and R6) | None | POST /api/jobs; PUT /api/jobs/{id} | Create_EngineerWithoutRequiredSkill_Throws422AndSavesNothing; Create_InactiveEngineerWithoutTimes_Throws422AndSavesNothing; Create_InactiveEngineerWithBothTimes_Throws422WithScheduledWording; Update_EngineerWithoutRequiredSkill_Throws422AndSavesNothing; Update_EngineerInactive_Throws422AndSavesNothing; Update_AllocatedWithoutEngineer_Throws422; Update_ScheduledWithoutBothTimes_Throws422WithWorkOrderWording; CreateJob_EngineerWithoutRequiredSkill_Returns422AndNothingIsPersisted (API); UpdateJob_EngineerWithoutRequiredSkill_Returns422AndJobIsUnchanged (API); Create_EngineerWithSkillAndBothTimes_IsScheduledInsideTransactionUnderEngineerLock; Create_EngineerWithoutTimes_IsAllocatedAndTakesNoEngineerLock; Create_NoEngineer_IsRequested. |
| Legacy POST and PUT /api/jobs apply the overlap rule (non-P1 409; P1 409 pointing to the schedule endpoint) | Reconciliation notes (same rule as R10) | None | POST /api/jobs; PUT /api/jobs/{id} | Create_NonP1OverlappingBooking_Throws409AndSavesNothing; Create_P1OverlappingBooking_Throws409PointingToScheduleEndpointAndSavesNothing; Create_AllocatedWithoutTimes_IsNotOverlapChecked; Update_ChangedTimesOverlappingAnotherBooking_Throws409AndSavesNothing; Update_P1OverlappingBooking_Throws409PointingToScheduleEndpointAndSavesNothing; Update_OnlyDescriptionChanged_SucceedsEvenWithLegacyOverlap; Update_LocksEngineerThenJobBeforeReadingJob; Update_UnknownJob_ThrowsNotFound. |
| Legacy PUT refuses a status change into OnSite, Completed or NotCompleted (409, use the visit endpoints), and refuses status, engineer or time changes on a job with visits (409) | Reconciliation notes | None | PUT /api/jobs/{id} | Update_MoveToVisitOnlyStatusOnJobWithNoVisits_Throws409AndSavesNothing (InlineData OnSite, Completed, NotCompleted); UpdateJob_MoveToCompletedThroughPut_Returns409AndJobIsUnchanged (API); Update_JobWithVisitsAndStatusChangeToAllocated_Throws409AndSavesNothing; Update_JobWithVisitsAndEngineerChange_Throws409; Update_JobWithVisitsAndTimesChange_Throws409; Update_JobWithVisitsAndUnchangedBookingAndNewDescription_Succeeds; Update_CompletedJobDescriptionEditWithSameStatus_Succeeds; Update_ScheduledToCancelledThroughPut_Succeeds. |
| Legacy PUT accepts only work-order statuses (InProgress is 400) | Reconciliation notes | None | PUT /api/jobs/{id} | UpdateJob_InProgressStatus_Returns400AndJobIsUnchanged (API); Validate_RecognisedStatus_IsValid; Validate_UnrecognisedStatus_IsRejectedOnStatus (JobUpdateRequestValidatorTests). |
| Half-supplied scheduled times refused (400: start and end both, or neither; end after start) | Reconciliation notes | None | POST /api/jobs; PUT /api/jobs/{id} | Create_StartWithoutEnd_IsRejectedWithBothOrNeitherMessage; Create_EndWithoutStart_IsRejectedWithBothOrNeitherMessage; Create_EndEqualToStart_IsRejectedWithEndAfterStartMessage; Create_EndBeforeStart_IsRejectedWithEndAfterStartMessage; Create_BothTimesWithNoEngineer_IsValid; Create_NeitherTime_IsValid; Create_BothTimesInOrder_IsValid; Update_StartWithoutEnd_IsRejectedWithBothOrNeitherMessage; Update_EndWithoutStart_IsRejectedWithBothOrNeitherMessage; Update_NeitherTime_IsValid; Update_BothTimesInOrder_IsValid; Update_EndBeforeStart_IsRejectedOnScheduledEnd; CreateJob_OnlyScheduledStart_Returns400AndJobCountIsUnchanged (API); UpdateJob_OnlyScheduledStart_Returns400AndJobIsUnchanged (API); CreateJob_RejectsEndBeforeStart (API). |
| Legacy job edits that change nothing relevant still succeed (no false block) | Reconciliation notes | None | PUT /api/jobs/{id} | Update_OnlyDescriptionChanged_SucceedsEvenWithLegacyOverlap; Update_LegacyJobWithNoVisits_FirstTimeFixFollowsOutcome; Cancel_ScheduledJob_SetsCancelledAndAddsReasonNote. |
| Pricing, invoicing, self-service, utilisation report | Out of scope | AC-17 (utilisation unchanged only) | None | No test specific to this work; see the First-time-fix rows above. |
| Call recording kept two years, customers not told (N2) | None. Not built. | None | None | None |
| Automatic supplier ordering (N1) | Manual version only: R27 to R28. R29 Not built. Supplier integration: Not built. | AC-15, AC-16 | As above | As above. No supplier test exists. |
| Booking warning for a part that is not coming (R29) | R29 | None | POST /schedule | Not built. No test. |
