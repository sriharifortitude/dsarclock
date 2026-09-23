# 1. Deadlines by Regulation 1182/71, and the earlier of two readings

Status: accepted — 2026-09-23

## Context

Art. 12(3) GDPR: "within one month of receipt of the request. That period
may be extended by two further months where necessary". The GDPR does not
define how a month is counted. Regulation (EEC, Euratom) No 1182/71 does,
for all EU acts:

- Art. 3(1): where a period is calculated from an event, the day of the
  event is not counted.
- Art. 3(2)(c): a period expressed in months ends with the expiry of the
  last hour of the day in the last month that has the same date as the day
  of the event; if the last month has no such date, the last day of that
  month.
- Art. 3(4): if the last day is a public holiday, Saturday or Sunday, the
  period ends with the expiry of the following working day.
- Art. 2(1): public holidays are those of the Member State where the act
  is to be done.

"Two further months" can be read as a total of three months from receipt,
or as two months running from the end of the first month. They coincide
unless the first month was cut short by a short month: received 31
January, the first month ends 28 February; three months from receipt is
30 April, two months from 28 February is 28 April.

## Decision

- Months are added by clamping to the last day of the target month, from
  the date of receipt, then moved to the next working day under Art. 3(4).
- The extended deadline is the **earlier** of the two readings, then moved
  to a working day. The weekend and holiday shift is applied only to the
  final date, never to the intermediate end of the first month.
- Holidays are the controller's jurisdiction's, plus dates the
  organisation adds for regional holidays.
- Easter is computed (Meeus/Jones/Butcher) rather than tabulated; the test
  suite checks 2024-2028 against published dates.

## Consequences

- The clock can be early compared to a regulator's view; it cannot be
  late compared to either reading. That is the right direction for the
  error.
- Every expected date in the tests was worked out by hand and checked
  against an independent calendar (weekday and Easter) before being
  written, so the tests encode the regulation, not the implementation.
- A regulator or court settling the extension reading would be a one-line
  change in `GdprDeadline.Extended` and one test.
