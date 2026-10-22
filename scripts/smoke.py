"""Exercise real HTTP integration against the explicitly seeded local demo.

Usage: python scripts/smoke.py [http://localhost:4200]
Requires only Python 3. Creates one identifiable demonstration ticket.
"""

import http.cookiejar
import json
import sys
import urllib.error
import urllib.request
from datetime import datetime, timezone


class Client:
    def __init__(self, base):
        self.base = base.rstrip("/") + "/api"
        self.opener = urllib.request.build_opener(
            urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar())
        )
        self.token = None

    def request(self, method, path, body=None, expected=200, raw=False):
        headers = {"Accept": "application/json"}
        data = None
        if body is not None:
            headers["Content-Type"] = "application/json"
            data = json.dumps(body).encode()
        if self.token:
            headers["X-XSRF-TOKEN"] = self.token
        request = urllib.request.Request(
            self.base + path, data=data, headers=headers, method=method
        )
        try:
            response = self.opener.open(request, timeout=30)
        except urllib.error.HTTPError as error:
            response = error
        payload = response.read()
        assert response.status == expected, (
            f"{method} {path}: expected {expected}, got {response.status}: "
            f"{payload.decode()[:500]}"
        )
        if raw:
            return payload.decode()
        return json.loads(payload) if payload else None

    def csrf(self):
        self.token = self.request("GET", "/auth/csrf")["token"]

    def login(self, email, slug="northstar"):
        self.csrf()
        user = self.request("POST", "/auth/login", {
            "organizationSlug": slug,
            "email": email,
            "password": "AvenorDemo!2026",
        })
        self.csrf()
        return user


def check(label):
    print(f"PASS {label}")


def main():
    base = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:4200"
    if not base.startswith(("http://localhost:", "http://127.0.0.1:")):
        raise SystemExit("This demonstration smoke test accepts loopback URLs only.")
    staff = Client(base)
    staff.request("GET", "/health")
    staff.request("GET", "/tickets", expected=401)
    staff.request("POST", "/auth/login", {}, expected=400)
    check("readiness, anonymous rejection and CSRF enforcement")
    admin = staff.login("alex@northstar.demo")
    assert admin["role"] == "Admin" and admin["organizationSlug"] == "northstar"
    check("organization login and authenticated antiforgery renewal")

    dashboard = staff.request("GET", "/dashboard?days=7")
    assert len(dashboard["volume"]) == 7
    customers = staff.request("GET", "/customers")
    customer = next(c for c in customers if c["email"] == "morgan@acme.demo")
    stamp = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%S")
    ticket = staff.request("POST", "/tickets", {
        "subject": f"Integration verification · {stamp}",
        "description": "A demonstration ticket created by the HTTP integration smoke test.",
        "priority": "Normal", "category": "Technical", "channel": "Portal",
        "customerId": customer["id"], "tags": ["verification"],
    }, expected=201)
    ticket_id = ticket["id"]
    detail = staff.request("GET", f"/tickets/{ticket_id}")
    ticket = detail["ticket"]
    updated = staff.request("PATCH", f"/tickets/{ticket_id}", {
        "version": ticket["version"], "priority": "High", "assigneeId": admin["id"],
    })
    staff.request("PATCH", f"/tickets/{ticket_id}", {
        "version": ticket["version"], "priority": "Urgent",
    }, expected=409)
    check("ticket persistence, assignment and optimistic concurrency")
    detail = staff.request("POST", f"/tickets/{ticket_id}/messages", {
        "version": updated["version"], "body": "Internal QA note: staff visibility only.",
        "isInternal": True,
    })
    detail = staff.request("POST", f"/tickets/{ticket_id}/messages", {
        "version": detail["ticket"]["version"], "body": "Thanks for contacting us. We are investigating.",
        "isInternal": False,
    })
    assert detail["ticket"]["firstRespondedAt"]
    resolved = staff.request("PATCH", f"/tickets/{ticket_id}", {
        "version": detail["ticket"]["version"], "status": "Resolved",
    })
    assert resolved["resolvedAt"]
    check("internal notes, first response and resolution timestamps")

    end_user = Client(base)
    end_user.login("morgan@acme.demo")
    visible = end_user.request("GET", f"/tickets/{ticket_id}")
    assert all(not m["isInternal"] for m in visible["messages"])
    end_user.request("POST", f"/tickets/{ticket_id}/messages", {
        "version": visible["ticket"]["version"], "body": "Forbidden private note", "isInternal": True,
    }, expected=403)
    end_user.request("GET", "/users", expected=403)
    reopened = end_user.request("POST", f"/tickets/{ticket_id}/messages", {
        "version": visible["ticket"]["version"], "body": "I have one more question about this issue.",
        "isInternal": False,
    })
    assert reopened["ticket"]["status"] == "Open"
    check("customer ownership, private-note filtering, role denial and reopening")

    foreign = Client(base)
    foreign.login("admin@meridian.demo", "meridian")
    foreign.request("GET", f"/tickets/{ticket_id}", expected=404)
    foreign.request("PATCH", f"/tickets/{ticket_id}", {
        "version": reopened["ticket"]["version"], "status": "Resolved",
    }, expected=404)
    check("cross-tenant reads and writes rejected")

    report = staff.request("GET", "/reports/export?days=30", raw=True)
    assert "Integration verification" in report
    audit = staff.request("GET", "/audit?page=1&pageSize=100")
    assert any(entry.get("ticketId") == ticket_id for entry in audit["items"])
    settings = staff.request("GET", "/settings")
    staff.request("PUT", "/settings", settings)
    created_user = staff.request("POST", "/users", {
        "name": "Integration verification agent",
        "email": f"smoke-{int(datetime.now(timezone.utc).timestamp() * 1000)}@northstar.demo",
        "role": "Agent", "password": "AvenorSmoke!2026",
    }, expected=201)
    deactivated = staff.request("PATCH", f"/users/{created_user['id']}", {
        "isActive": False,
    })
    assert not deactivated["isActive"]
    notifications = staff.request("GET", "/notifications")
    if notifications:
        staff.request("POST", "/notifications/read", {"ids": [notifications[0]["id"]]}, expected=204)
    check("CSV, audit trail, settings, transactional user administration and notifications")
    staff.request("POST", "/auth/logout", {}, expected=204)
    staff.request("GET", "/auth/me", expected=401)
    check("logout invalidates the session")
    print(f"Verified real API at {base}; demonstration ticket #{ticket['number']}.")


if __name__ == "__main__":
    main()
