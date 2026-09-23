-- Applied at startup under an advisory lock (see Data.cs). Idempotent.

create table if not exists requests (
    id                uuid primary key,
    type              text not null,
    subject_reference text not null check (length(subject_reference) between 1 and 64),
    received_on       date not null,
    jurisdiction      text not null,
    status            text not null,
    extended          boolean not null default false,
    closed_on         date
);

create index if not exists requests_open on requests (received_on) where status in ('Open', 'AwaitingIdentity');

-- The audit trail. Append-only, enforced by the database rather than by
-- convention: a supervisory authority asking "when did you extend this?"
-- must get the answer that was true at the time.
create table if not exists request_events (
    id          bigint generated always as identity primary key,
    request_id  uuid not null references requests (id),
    on_date     date not null,
    kind        text not null,
    detail      text not null,
    actor       text not null,
    recorded_at timestamptz not null default now()
);

create index if not exists request_events_by_request on request_events (request_id, id);

create or replace function request_events_append_only() returns trigger language plpgsql as $$
begin
    raise exception 'request_events is append-only (% refused)', tg_op using errcode = 'insufficient_privilege';
end $$;

drop trigger if exists request_events_no_change on request_events;
create trigger request_events_no_change before update or delete on request_events
    for each row execute function request_events_append_only();

-- Regional and company holidays on top of the national calendar.
create table if not exists extra_holidays (
    jurisdiction text not null,
    day          date not null,
    note         text not null default '',
    primary key (jurisdiction, day)
);
