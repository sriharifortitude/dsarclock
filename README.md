# dsarclock

[![CI](https://github.com/sriharifortitude/dsarclock/actions/workflows/ci.yml/badge.svg)](https://github.com/sriharifortitude/dsarclock/actions/workflows/ci.yml)

A register and deadline clock for GDPR data subject requests — access,
erasure, rectification, restriction, portability, objection — built so
that a data protection officer can answer the supervisory authority's
first two questions from it: *when was it due?* and *what did you do,
when?*

## The part that is easy to get wrong

Art. 12(3) gives a controller "one month of receipt", extendable "by two
further months". How a month is counted in EU law is fixed by
**Regulation 1182/71** on periods, dates and time limits, and most tools
count it as "30 days" or "same date next month" and stop there:

| received | due | why |
| --- | --- | --- |
| 15 Mar 2026 | **15 Apr 2026** | same date next month; the day of receipt is not counted |
| 31 Jan 2026 | **2 Mar 2026** | February has no 31st → 28 Feb; that is a Saturday → next working day |
| 14 Apr 2026 | **15 May 2026** | 14 May is Ascension Day in Germany → Friday |
| 25 Nov 2026 | **28 Dec 2026** | 25th and 26th are holidays, 26th and 27th a weekend |
| 4 May 2026 (AT) | **5 Jun 2026** | Corpus Christi is a national holiday in Austria, not in Germany |
| 31 Jan 2026, extended | **28 Apr 2026** | see below |

The extension has two defensible readings — three months from receipt
(30 April) or two months from the end of the first month (28 April).
dsarclock uses the earlier. A compliance tool that has to guess should
guess the date that cannot be late. ([ADR 1](docs/adr/0001-deadline-arithmetic.md))

Holidays are the **controller's** Member State's (1182/71 Art. 2(1)):
national calendars for Germany, Austria and France with Easter computed
from first principles, plus any regional or company days you add
(`PUT /api/holidays/DE/2027-01-06` for a Bavarian office).

Every expected date in the test suite was worked out by hand and checked
against an independent calendar before being written down.

## The rules it enforces

Each change is a method on the request that checks the Art. 12 rule it
touches and appends to an audit trail:

- **An extension** needs reasons (the data subject must receive them), can
  happen once, and must be made within the first month — afterwards it is
  refused with *"Art. 12(3): the data subject must be told of an extension
  within one month; that ended 2026-03-02"*.
- **A refusal** (manifestly unfounded, excessive, identity not
  established, Art. 23 restriction) needs an explanation, and is judged
  against the **original** month even if the request was extended — Art.
  12(4) says "at the latest within one month of receipt".
- **Completion** is judged against the extended deadline if there is one.
  Late completions and refusals are recorded as late, with the date they
  were due, not silently accepted.
- **Identity checks** (Art. 12(6)) block completion but do not stop the
  clock. ([ADR 2](docs/adr/0002-identity-does-not-pause.md))

## What makes the register trustworthy

- **The audit trail is append-only in the database**: a trigger refuses
  `UPDATE` and `DELETE` on the event table, so not even someone with SQL
  access can rewrite "when did you extend this?". The test tries.
- **Concurrent edits conflict** instead of overwriting: Postgres's `xmin`
  is the EF Core concurrency token; two people acting on the same request
  at once get a 409.
- **Minimal personal data.** Requests carry a reference into your own
  CRM, not a name or an email. A register of who exercised their rights is
  itself personal data.
- **"Today" is the controller's today**, in a configured time zone,
  through an injected `TimeProvider` — which is also how the tests pin the
  date.

## API

    POST /api/requests                                {type, subjectReference, receivedOn, jurisdiction, channel}
    GET  /api/requests?overdue=true&dueWithinWorkingDays=5    open requests, soonest deadline first
    GET  /api/requests/{id}                            with deadlines, working days left, full event history
    POST /api/requests/{id}/identity-requested         {text}
    POST /api/requests/{id}/identity-confirmed
    POST /api/requests/{id}/extend                     {text: reasons}
    POST /api/requests/{id}/complete                   {text: what was provided}
    POST /api/requests/{id}/refuse                     {ground, explanation}
    PUT  /api/holidays/{jurisdiction}/{date}           {text}
    GET  /api/holidays/{jurisdiction}/{year}

`Authorization: Bearer <token>` (32+ characters, `DsarClock__ApiToken`,
compared in constant time); `X-Actor` names who acted and is written into
every event. A rule violation is a 422 problem detail whose `detail` names
the article. `/healthz` stays open for probes.

## Running it

    docker compose up -d                                   # Postgres on 127.0.0.1:5438
    export ConnectionStrings__Default="Host=127.0.0.1;Port=5438;Database=dsarclock;Username=dsarclock;Password=dsarclock_local_dev"
    export DsarClock__ApiToken=$(openssl rand -hex 24) DsarClock__TimeZone=Europe/Berlin
    dotnet run --project src/DsarClock.Api

The schema is applied at start under an advisory lock, so several
replicas can start together. The image (`docker build -t dsarclock .`) is
.NET's chiseled ASP.NET base: no shell, non-root.

## Checks

    dotnet build        # nullable, analyzers at latest-recommended, warnings as errors
    dotnet test         # 24 domain tests; 8 HTTP tests against Postgres via Testcontainers (needs Docker)

## What it deliberately does not do

- **It is not legal advice**, and the extension reading and the identity
  rule are choices, documented as such in the ADRs.
- **Three jurisdictions.** DE, AT, FR national holidays. Adding one is a
  list of dates and Easter offsets; Ireland's "first Monday in…" rules and
  substitute days would need a little more.
- **No regional calendars built in.** Sixteen German Länder are sixteen
  lists; they are added per organisation instead.
- **No correspondence.** It records that the subject was told; it does not
  send the letter.
- **No per-user accounts.** One token plus an `X-Actor` header set by the
  SSO proxy in front of it.

## Licence

Business Source License 1.1. Free for evaluation, development and
non-commercial use; production use needs a commercial licence. Converts to
Apache 2.0 on 2030-09-23. See [LICENSE](LICENSE).
