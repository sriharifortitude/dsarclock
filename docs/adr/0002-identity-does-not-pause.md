# 2. Identity checks do not stop the clock

Status: accepted — 2026-09-23

## Context

Art. 12(6) lets a controller with reasonable doubts about identity request
additional information. Practice varies on whether the one-month period
then pauses until the information arrives. Some supervisory authorities
accept a pause; others treat the period as running from receipt and
expect the identity request to be made promptly.

## Decision

The clock runs from receipt regardless. Requesting identity information
moves the request to `AwaitingIdentity`, which blocks completion and is
visible in the register, but does not change the deadline. A request left
waiting for identity past its deadline shows as overdue.

## Consequences

- The tool never tells a DPO they have time they might not have.
- A controller whose authority accepts a pause will see requests marked
  overdue that are not, and can extend (within the first month) or refuse
  on Art. 12(2) grounds when identity cannot be established. That is more
  work than a pause; it is also the reading that cannot be wrong.
- Making the pause a per-organisation setting is a small change in
  `DsarRequest.Deadline`; it is not offered by default because the safer
  default is the one people keep.
