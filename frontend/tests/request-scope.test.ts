import test from "node:test";
import assert from "node:assert/strict";
import { RequestScope } from "../src/app/request-scope.ts";

test("a newer route/filter response supersedes an earlier page request", () => {
  const requests = new RequestScope();
  const firstFilter = requests.begin("page");
  const latestFilter = requests.begin("page");
  assert.equal(requests.isCurrent(firstFilter), false);
  assert.equal(requests.isCurrent(latestFilter), true);
});

test("session reset rejects every old read and mutation even if lane counters repeat", () => {
  const requests = new RequestScope();
  const reads = ["page", "people", "notifications", "mutation"].map((lane) =>
    requests.begin(lane),
  );
  requests.invalidate();
  const newSession = requests.begin("page");
  assert.ok(reads.every((token) => !requests.isCurrent(token)));
  assert.equal(requests.isCurrent(newSession), true);
});

test("notification and people refreshes do not cancel the current page request", () => {
  const requests = new RequestScope();
  const page = requests.begin("page");
  requests.begin("people");
  requests.begin("notifications");
  assert.equal(requests.isCurrent(page), true);
});

test("a delayed old-tenant completion cannot overwrite the new tenant workspace", async () => {
  const requests = new RequestScope();
  let release!: (value: string) => void;
  let rendered = "";
  const old = requests.begin("page");
  const delayed = new Promise<string>((resolve) => {
    release = resolve;
  });
  const pending = delayed.then((value) => {
    if (requests.isCurrent(old)) rendered = value;
  });
  requests.invalidate();
  rendered = "new tenant";
  release("old tenant");
  await pending;
  assert.equal(rendered, "new tenant");
});
