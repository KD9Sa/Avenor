import test from "node:test";
import assert from "node:assert/strict";
import {
  allowedTransitions,
  chartPath,
  initials,
  relativeTime,
  slaState,
  validateTicket,
} from "../src/app/helpers.ts";

test("resolved ticket can reopen or close and closed ticket is terminal", () => {
  assert.deepEqual(allowedTransitions("Resolved"), ["Open", "Closed"]);
  assert.deepEqual(allowedTransitions("Closed"), []);
  assert.ok(!allowedTransitions("Open").includes("Closed"));
});
test("ticket validation enforces subject bounds and required customer for staff", () => {
  assert.equal(
    validateTicket("a".repeat(180), "Helpful description", true, "customer-id"),
    null,
  );
  assert.match(
    validateTicket(
      "a".repeat(181),
      "Helpful description",
      true,
      "customer-id",
    )!,
    /180/,
  );
  assert.match(
    validateTicket("  no  ", "Helpful description", false, "")!,
    /subject/,
  );
  assert.match(
    validateTicket("A valid subject", "short", false, "")!,
    /Describe/,
  );
  assert.match(
    validateTicket("A valid subject", "Helpful description", true, "")!,
    /customer/,
  );
  assert.equal(
    validateTicket("A valid subject", "Helpful description", false, ""),
    null,
  );
});
test("SLA switches from response to resolution target after first response", () => {
  const now = Date.parse("2026-10-22T12:00:00Z");
  const ticket = {
    status: "Open",
    firstRespondedAt: null as string | null,
    responseDueAt: "2026-10-22T11:00:00Z",
    resolutionDueAt: "2026-10-22T18:00:00Z",
  };
  assert.equal(slaState(ticket, now).state, "danger");
  ticket.firstRespondedAt = "2026-10-22T10:30:00Z";
  assert.deepEqual(slaState(ticket, now), { label: "6h left", state: "muted" });
  ticket.status = "Resolved";
  assert.deepEqual(slaState(ticket, now), {
    label: "Completed",
    state: "success",
  });
});
test("approaching SLA deadline is warning, overdue is danger", () => {
  const now = Date.parse("2026-10-22T12:00:00Z");
  const ticket = {
    status: "Pending",
    firstRespondedAt: null,
    responseDueAt: "2026-10-22T12:45:00Z",
    resolutionDueAt: "2026-10-23T12:00:00Z",
  };
  assert.deepEqual(slaState(ticket, now), {
    label: "45m left",
    state: "warning",
  });
});
test("chart coordinates avoid division by zero for empty or single-point series", () => {
  assert.equal(chartPath([], 600, 150, 0), "");
  assert.equal(chartPath([0], 600, 150, 0), "M0.0,150.0");
  assert.equal(chartPath([0, 10], 600, 150, 10), "M0.0,150.0 L600.0,0.0");
});
test("identity and time labels handle whitespace, future timestamps and elapsed days", () => {
  assert.equal(initials("  Alex   Morgan  "), "AM");
  assert.equal(initials(""), "");
  assert.equal(
    relativeTime("2026-10-23T12:00:00Z", Date.parse("2026-10-22T12:00:00Z")),
    "Just now",
  );
  assert.equal(
    relativeTime("2026-10-20T12:00:00Z", Date.parse("2026-10-22T12:00:00Z")),
    "2d ago",
  );
});
