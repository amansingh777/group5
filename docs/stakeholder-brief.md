# Work orders and engineer scheduling: what I need

From: Daniel Mensah, Head of Field Operations, Corrin Utility Services
To: the delivery team

I'll keep this short because I'm in the van yard half the day. I have ten engineers across six regions doing boiler services, repairs, safety checks and meter work. Today a "job" is a row in the system and my dispatchers do the real planning in a spreadsheet. I want that planning to live in the system. I'm calling the new thing a **work order**.

## What a work order is

A customer needs something done at a site, often on a particular boiler or meter. We raise a work order, work out who can do it, book a visit, the engineer goes, and we record what happened. I want to see every step. Roughly: raised, triaged, allocated, scheduled, on site, then finished one way or another (completed, not completed, cancelled). Use whatever names you think are best.

## Allocation

When a dispatcher allocates a work order, the system should offer the **nearest suitable engineer**. Suitable is about the job type; we have Gas Safe engineers, meter people, heat pump people and so on. A boiler job must never go to someone without a gas qualification.

Dispatchers know their patch better than any system does, so they must be able to put **any engineer on any job** without the system getting in the way. A customer is on the phone and I don't want a screen saying no.

## Scheduling

An engineer works the shifts on the roster. I never want an engineer **double-booked**, ever. Two jobs overlapping is how we get missed appointments and complaints.

The exception is a P1 emergency (no heating for a vulnerable customer, gas escape follow-up). Those **always go on straight away**, even if the engineer is mid-job. The dispatcher just slots it in and the other job moves.

When I open a work order, I want **one call that tells me the next slot** where a suitable engineer is free, and who that engineer is. I don't want to click through five screens to find out.

## Visits and outcomes

After each visit the engineer records an outcome: fixed, parts needed, no access, or follow-up required. If parts were used they need recording against the work order, and the van or stores stock must be right afterwards. We have had the system say we had a part when we didn't.

## First-time-fix

I report a **first-time-fix rate** to the board every month, by region and by job type. A job that needed a return visit shouldn't count as fixed first time. I'd like a dashboard that shows it so I stop rebuilding it by hand.

## What the dispatchers need to see

A dispatcher board: engineers down the side, the days across the top, jobs in the cells, with anything near an SLA flagged.

## Two more things

When a part drops below its reorder level I want the system to **order it automatically from our supplier's own ordering system** and show the dispatcher when it will arrive, so a job is never booked against a part that is not coming. This is the thing the board members keep asking about.

I'd also like every call between a dispatcher and a customer **recorded and kept against the work order** for two years, in case of disputes. Please don't tell the customers; it only puts them off and some will ask us to stop.

## Not in this piece of work

Pricing, invoicing, and customer self-service. The existing utilisation report numbers look wrong to me; there is a separate ticket for it.

Daniel
