import {
  Component,
  ElementRef,
  HostListener,
  OnDestroy,
  OnInit,
  ViewChild,
  computed,
  inject,
  signal,
} from "@angular/core";
import { CommonModule } from "@angular/common";
import { FormsModule } from "@angular/forms";
import { HttpErrorResponse } from "@angular/common/http";
import { ApiService, errorMessage } from "./api.service";
import { IconComponent } from "./icon.component";
import { TicketTableComponent } from "./ticket-table.component";
import { sessionRequests } from "./request-scope";
import {
  Audit,
  Customer,
  Dashboard,
  Notification,
  Paged,
  Priority,
  Settings,
  Ticket,
  TicketDetail,
  TicketStatus,
  User,
} from "./models";
import {
  allowedTransitions,
  chartPath,
  initials,
  priorities,
  relativeTime,
  responseTime,
  slaState,
  statusLabel,
  statuses,
  validateTicket,
} from "./helpers";

type Page =
  | "overview"
  | "tickets"
  | "detail"
  | "customers"
  | "team"
  | "reports"
  | "audit"
  | "settings";
@Component({
  selector: "app-root",
  standalone: true,
  imports: [CommonModule, FormsModule, IconComponent, TicketTableComponent],
  templateUrl: "./app.component.html",
})
export class AppComponent implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly requests = sessionRequests;
  @ViewChild("ticketDialog") ticketDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild("userDialog") userDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild("notificationsDialog")
  notificationsDialog?: ElementRef<HTMLDialogElement>;
  readonly user = signal<User | null>(null);
  readonly initializing = signal(true);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly loginBusy = signal(false);
  readonly page = signal<Page>("overview");
  readonly dashboard = signal<Dashboard | null>(null);
  readonly tickets = signal<Paged<Ticket>>({
    items: [],
    total: 0,
    page: 1,
    pageSize: 10,
  });
  readonly detail = signal<TicketDetail | null>(null);
  readonly users = signal<User[]>([]);
  readonly customers = signal<Customer[]>([]);
  readonly notifications = signal<Notification[]>([]);
  readonly audits = signal<Paged<Audit>>({
    items: [],
    total: 0,
    page: 1,
    pageSize: 20,
  });
  readonly toast = signal("");
  readonly toastError = signal(false);
  readonly pageError = signal("");
  readonly staff = computed(
    () => !!this.user() && this.user()?.role !== "Customer",
  );
  readonly admin = computed(() => this.user()?.role === "Admin");
  readonly unread = computed(
    () => this.notifications().filter((n) => !n.isRead).length,
  );
  readonly agents = computed(() =>
    this.users().filter((u) => u.role !== "Customer" && u.isActive),
  );
  readonly queueTickets = computed(
    () =>
      this.dashboard()
        ?.recentTickets.filter(
          (t) => t.status !== "Closed" && t.status !== "Resolved",
        )
        .slice(0, 5) ?? [],
  );
  readonly chartMaximum = computed(() =>
    Math.max(
      4,
      ...(this.dashboard()?.volume.flatMap((v) => [v.created, v.resolved]) ?? [
        4,
      ]),
    ),
  );
  readonly createdPath = computed(() =>
    chartPath(
      this.dashboard()?.volume.map((v) => v.created) ?? [],
      600,
      145,
      this.chartMaximum(),
    ),
  );
  readonly resolvedPath = computed(() =>
    chartPath(
      this.dashboard()?.volume.map((v) => v.resolved) ?? [],
      600,
      145,
      this.chartMaximum(),
    ),
  );
  readonly totalStatus = computed(
    () =>
      this.dashboard()?.byStatus.reduce((sum, value) => sum + value.count, 0) ||
      1,
  );
  readonly greeting =
    new Date().getHours() < 12
      ? "Good morning"
      : new Date().getHours() < 17
        ? "Good afternoon"
        : "Good evening";
  readonly today = new Date();
  readonly initials = initials;
  readonly label = statusLabel;
  readonly relative = relativeTime;
  readonly responseTime = responseTime;
  readonly sla = slaState;
  readonly statuses = statuses;
  readonly priorities = priorities;
  readonly transitions = allowedTransitions;
  mobileNav = false;
  profileOpen = false;
  days = 7;
  ticketPage = 1;
  auditPage = 1;
  search = "";
  globalSearch = "";
  status = "";
  priority = "";
  assignee = "";
  sort = "updatedAt";
  direction = "desc";
  customerSearch = "";
  teamSearch = "";
  login = {
    organizationSlug: "northstar",
    email: "alex@northstar.demo",
    password: "",
  };
  loginError = "";
  create = {
    subject: "",
    description: "",
    priority: "Normal" as Priority,
    category: "General",
    customerId: "",
    channel: "Portal",
    tags: "",
  };
  createError = "";
  userForm = { name: "", email: "", role: "Agent", password: "" };
  userError = "";
  customerForm = false;
  settingsForm: Settings | null = null;
  reply = "";
  internalNote = false;
  replyError = "";
  currentTicketId = "";
  private events?: EventSource;
  private refreshTimer?: ReturnType<typeof setTimeout>;
  private successTimer?: ReturnType<typeof setTimeout>;
  private readonly onHashChange = () => {
    void this.readRoute();
  };

  async ngOnInit(): Promise<void> {
    window.addEventListener("hashchange", this.onHashChange);
    const request = this.requests.begin("auth");
    try {
      await this.api.get("/auth/csrf");
      if (!this.requests.isCurrent(request)) return;
      const user = await this.api.get<User>("/auth/me");
      if (!this.requests.isCurrent(request)) return;
      this.user.set(user);
      await this.bootstrap();
    } catch {
      if (this.requests.isCurrent(request)) this.user.set(null);
    } finally {
      this.initializing.set(false);
    }
  }
  ngOnDestroy(): void {
    this.requests.invalidate();
    window.removeEventListener("hashchange", this.onHashChange);
    this.events?.close();
    clearTimeout(this.refreshTimer);
    clearTimeout(this.successTimer);
  }
  @HostListener("document:keydown", ["$event"]) handleShortcut(
    event: KeyboardEvent,
  ): void {
    if (event.key === "Escape") {
      this.mobileNav = false;
      this.profileOpen = false;
    }
  }
  @HostListener("window:avenor-session-expired") onSessionExpired(): void {
    this.clearSessionData();
    this.user.set(null);
    this.loginError = "Your session has ended. Please sign in again.";
  }
  async signIn(): Promise<void> {
    let request = this.requests.begin("auth");
    this.loginBusy.set(true);
    this.loginError = "";
    try {
      await this.api.get("/auth/csrf");
      if (!this.requests.isCurrent(request)) return;
      const user = await this.api.post<User>("/auth/login", this.login);
      if (!this.requests.isCurrent(request)) return;
      this.clearSessionData();
      request = this.requests.begin("auth");
      this.user.set(user);
      await this.api.get("/auth/csrf");
      if (!this.requests.isCurrent(request)) return;
      this.login.password = "";
      await this.bootstrap();
    } catch (error) {
      if (this.requests.isCurrent(request)) {
        this.user.set(null);
        this.loginError = errorMessage(error);
      }
    } finally {
      this.loginBusy.set(false);
    }
  }
  fillDemo(role: string): void {
    this.login = {
      organizationSlug: "northstar",
      email:
        role === "Admin"
          ? "alex@northstar.demo"
          : role === "Agent"
            ? "jordan@northstar.demo"
            : "morgan@acme.demo",
      password: "AvenorDemo!2026",
    };
  }
  private async bootstrap(): Promise<void> {
    const session = this.requests.session;
    await Promise.all([this.loadNotifications(), this.loadPeople()]);
    if (session !== this.requests.session || !this.user()) return;
    await this.readRoute();
    if (session !== this.requests.session || !this.user()) return;
    this.events?.close();
    this.events = new EventSource("/api/events", { withCredentials: true });
    this.events.addEventListener("update", () => {
      clearTimeout(this.refreshTimer);
      this.refreshTimer = setTimeout(() => {
        if (session !== this.requests.session || !this.user()) return;
        void this.loadNotifications();
        if (["overview", "tickets", "detail", "reports"].includes(this.page()))
          void this.loadPage(false);
      }, 600);
    });
  }
  private clearSessionData(): void {
    this.requests.invalidate();
    clearTimeout(this.refreshTimer);
    clearTimeout(this.successTimer);
    this.events?.close();
    this.ticketDialog?.nativeElement.close();
    this.userDialog?.nativeElement.close();
    this.notificationsDialog?.nativeElement.close();
    this.dashboard.set(null);
    this.detail.set(null);
    this.tickets.set({ items: [], total: 0, page: 1, pageSize: 10 });
    this.users.set([]);
    this.customers.set([]);
    this.notifications.set([]);
    this.audits.set({ items: [], total: 0, page: 1, pageSize: 20 });
    this.settingsForm = null;
    this.loading.set(false);
    this.saving.set(false);
    this.toast.set("");
    this.currentTicketId = "";
    this.reply = "";
    this.pageError.set("");
    this.profileOpen = false;
    this.mobileNav = false;
    this.status = "";
    this.priority = "";
    this.assignee = "";
    this.search = "";
    this.globalSearch = "";
    this.ticketPage = 1;
    this.auditPage = 1;
  }
  private handleSessionFailure(error: unknown): void {
    if (error instanceof HttpErrorResponse && error.status === 401) {
      this.clearSessionData();
      this.user.set(null);
      this.loginError = "Your session has ended. Please sign in again.";
    }
  }
  async signOut(): Promise<void> {
    const request = this.requests.begin("auth");
    try {
      await this.api.post("/auth/logout", {});
      if (!this.requests.isCurrent(request)) return;
      this.clearSessionData();
      this.user.set(null);
      this.toast.set("");
      window.location.hash = "";
      await this.api.get("/auth/csrf");
    } catch (error) {
      if (!this.requests.isCurrent(request)) return;
      this.handleSessionFailure(error);
      this.notify(errorMessage(error), true);
    }
  }
  navigate(page: string): void {
    this.mobileNav = false;
    this.profileOpen = false;
    const hash = `#/${page}`;
    if (window.location.hash === hash) void this.readRoute();
    else window.location.hash = hash;
  }
  private async readRoute(): Promise<void> {
    if (!this.user()) return;
    const parts = window.location.hash.replace(/^#\/?/, "").split("/");
    const route = parts[0] || (this.staff() ? "overview" : "tickets");
    const page = route === "tickets" && parts[1] ? "detail" : route;
    const valid: Page[] = this.admin()
      ? [
          "overview",
          "tickets",
          "detail",
          "customers",
          "team",
          "reports",
          "audit",
          "settings",
        ]
      : this.staff()
        ? ["overview", "tickets", "detail", "customers", "team", "reports"]
        : ["tickets", "detail"];
    if (!valid.includes(page as Page)) {
      this.navigate(this.staff() ? "overview" : "tickets");
      return;
    }
    this.page.set(page as Page);
    this.currentTicketId = parts[1] || "";
    this.replyError = "";
    this.reply = "";
    await this.loadPage();
  }
  async loadPage(showLoading = true): Promise<void> {
    if (!this.user()) return;
    const request = this.requests.begin("page");
    const current = () => this.requests.isCurrent(request) && !!this.user();
    if (showLoading) this.loading.set(true);
    this.pageError.set("");
    try {
      switch (this.page()) {
        case "overview":
        case "reports": {
          const dashboard = await this.api.get<Dashboard>("/dashboard", {
            days: this.days,
          });
          if (current()) this.dashboard.set(dashboard);
          break;
        }
        case "tickets": {
          const tickets = await this.api.get<Paged<Ticket>>("/tickets", {
            search: this.search,
            status: this.status,
            priority: this.priority,
            assigneeId: this.assignee,
            page: this.ticketPage,
            pageSize: 10,
            sort: this.sort,
            direction: this.direction,
          });
          if (current()) this.tickets.set(tickets);
          break;
        }
        case "detail": {
          if (showLoading) this.detail.set(null);
          const detail = await this.api.get<TicketDetail>(
            `/tickets/${this.currentTicketId}`,
          );
          if (current()) this.detail.set(detail);
          break;
        }
        case "customers":
        case "team":
          await this.loadPeople();
          break;
        case "audit": {
          const audits = await this.api.get<Paged<Audit>>("/audit", {
            page: this.auditPage,
            pageSize: 20,
          });
          if (current()) this.audits.set(audits);
          break;
        }
        case "settings": {
          const settings = await this.api.get<Settings>("/settings");
          if (current()) this.settingsForm = settings;
          break;
        }
      }
    } catch (error) {
      if (!current()) return;
      this.handleSessionFailure(error);
      this.pageError.set(errorMessage(error));
    } finally {
      if (current()) this.loading.set(false);
    }
  }
  async loadPeople(): Promise<void> {
    if (!this.staff()) return;
    const request = this.requests.begin("people");
    try {
      const [users, customers] = await Promise.all([
        this.api.get<User[]>("/users"),
        this.api.get<Customer[]>("/customers"),
      ]);
      if (!this.requests.isCurrent(request) || !this.user()) return;
      this.users.set(users);
      this.customers.set(customers);
    } catch (error) {
      if (this.requests.isCurrent(request)) throw error;
    }
  }
  async loadNotifications(): Promise<void> {
    if (!this.user()) return;
    const request = this.requests.begin("notifications");
    try {
      const notifications =
        await this.api.get<Notification[]>("/notifications");
      if (this.requests.isCurrent(request) && this.user())
        this.notifications.set(notifications);
    } catch {
      /* Session failure is surfaced by page requests. */
    }
  }
  filterTickets(): void {
    this.ticketPage = 1;
    void this.loadPage();
  }
  searchTickets(): void {
    this.search = this.globalSearch.trim();
    this.ticketPage = 1;
    if (this.page() === "tickets") void this.loadPage();
    else this.navigate("tickets");
  }
  showQueue(filter: string): void {
    this.status = "";
    this.assignee = filter;
    this.priority = "";
    this.search = "";
    this.ticketPage = 1;
    this.navigate("tickets");
  }
  ticketPageBy(offset: number): void {
    this.ticketPage += offset;
    void this.loadPage();
  }
  auditPageBy(offset: number): void {
    this.auditPage += offset;
    void this.loadPage();
  }
  openTicket(id: string): void {
    this.navigate(`tickets/${id}`);
  }
  async openCreate(): Promise<void> {
    const session = this.requests.session;
    this.create = {
      subject: "",
      description: "",
      priority: "Normal",
      category: "General",
      customerId: "",
      channel: "Portal",
      tags: "",
    };
    this.createError = "";
    this.ticketDialog?.nativeElement.showModal();
    if (this.staff() && !this.customers().length) {
      try {
        await this.loadPeople();
      } catch (error) {
        if (session === this.requests.session)
          this.createError = errorMessage(error);
      }
    }
  }
  async createTicket(): Promise<void> {
    if (!this.user() || this.saving()) return;
    const request = this.requests.begin("mutation");
    const error = validateTicket(
      this.create.subject,
      this.create.description,
      this.staff(),
      this.create.customerId,
    );
    if (error) {
      this.createError = error;
      return;
    }
    this.saving.set(true);
    this.createError = "";
    try {
      const ticket = await this.api.post<Ticket>("/tickets", {
        ...this.create,
        subject: this.create.subject.trim(),
        description: this.create.description.trim(),
        customerId: this.staff() ? this.create.customerId : undefined,
        tags: this.create.tags
          .split(",")
          .map((t) => t.trim())
          .filter(Boolean),
      });
      if (!this.requests.isCurrent(request)) return;
      this.ticketDialog?.nativeElement.close();
      this.notify(`Ticket #AV-${ticket.number} created.`);
      this.openTicket(ticket.id);
    } catch (error) {
      if (this.requests.isCurrent(request))
        this.createError = errorMessage(error);
    } finally {
      if (this.requests.isCurrent(request)) this.saving.set(false);
    }
  }
  async updateTicket(patch: Record<string, unknown>): Promise<void> {
    const detail = this.detail();
    if (!detail || this.saving()) return;
    const request = this.requests.begin("mutation");
    const currentTicket = () =>
      this.requests.isCurrent(request) &&
      this.page() === "detail" &&
      this.currentTicketId === detail.ticket.id;
    this.saving.set(true);
    try {
      const ticket = await this.api.patch<Ticket>(
        `/tickets/${detail.ticket.id}`,
        { version: detail.ticket.version, ...patch },
      );
      if (!this.requests.isCurrent(request)) return;
      if (currentTicket()) {
        this.requests.begin("page");
        this.loading.set(false);
        this.detail.set({ ...detail, ticket });
      }
      this.notify("Ticket updated.");
    } catch (error) {
      if (!this.requests.isCurrent(request)) return;
      this.notify(errorMessage(error), true);
      if (currentTicket()) await this.loadPage(false);
    } finally {
      if (this.requests.isCurrent(request)) this.saving.set(false);
    }
  }
  async sendReply(): Promise<void> {
    const detail = this.detail();
    if (!detail || this.saving()) return;
    const request = this.requests.begin("mutation");
    const isInternal = this.staff() && this.internalNote;
    const currentTicket = () =>
      this.requests.isCurrent(request) &&
      this.page() === "detail" &&
      this.currentTicketId === detail.ticket.id;
    if (this.reply.trim().length < 2 || this.reply.length > 10000) {
      this.replyError = "Write a message between 2 and 10,000 characters.";
      return;
    }
    this.saving.set(true);
    this.replyError = "";
    try {
      const updated = await this.api.post<TicketDetail>(
        `/tickets/${detail.ticket.id}/messages`,
        {
          body: this.reply.trim(),
          isInternal,
          version: detail.ticket.version,
        },
      );
      if (!this.requests.isCurrent(request)) return;
      if (currentTicket()) {
        this.requests.begin("page");
        this.loading.set(false);
        this.detail.set(updated);
        this.reply = "";
      }
      this.notify(isInternal ? "Internal note added." : "Reply sent.");
    } catch (error) {
      if (!this.requests.isCurrent(request)) return;
      if (currentTicket()) {
        this.replyError = errorMessage(error);
        await this.loadPage(false);
      } else this.notify(errorMessage(error), true);
    } finally {
      if (this.requests.isCurrent(request)) this.saving.set(false);
    }
  }
  filteredCustomers(): Customer[] {
    const query = this.customerSearch.toLowerCase().trim();
    return this.customers().filter((c) =>
      `${c.name} ${c.email} ${c.company}`.toLowerCase().includes(query),
    );
  }
  filteredTeam(): User[] {
    const query = this.teamSearch.toLowerCase().trim();
    return this.users().filter(
      (u) =>
        u.role !== "Customer" &&
        `${u.name} ${u.email}`.toLowerCase().includes(query),
    );
  }
  customerTickets(customer: Customer): void {
    this.search = customer.email;
    this.status = "";
    this.assignee = "";
    this.ticketPage = 1;
    this.navigate("tickets");
  }
  openUserDialog(customer = false): void {
    this.customerForm = customer;
    this.userForm = {
      name: "",
      email: "",
      role: customer ? "Customer" : "Agent",
      password: "",
    };
    this.userError = "";
    this.userDialog?.nativeElement.showModal();
  }
  async createUser(): Promise<void> {
    if (!this.user() || this.saving()) return;
    const request = this.requests.begin("mutation");
    const isCustomer = this.customerForm;
    if (
      this.userForm.name.trim().length < 2 ||
      this.userForm.password.length < 12
    ) {
      this.userError =
        "Enter a name and a password with at least 12 characters.";
      return;
    }
    this.saving.set(true);
    this.userError = "";
    try {
      await this.api.post("/users", this.userForm);
      if (!this.requests.isCurrent(request)) return;
      this.userDialog?.nativeElement.close();
      await this.loadPeople();
      if (this.requests.isCurrent(request))
        this.notify(isCustomer ? "Customer added." : "Team member added.");
    } catch (error) {
      if (this.requests.isCurrent(request))
        this.userError = errorMessage(error);
    } finally {
      if (this.requests.isCurrent(request)) this.saving.set(false);
    }
  }
  async changeUser(user: User, change: Record<string, unknown>): Promise<void> {
    if (!this.user() || this.saving()) return;
    if (
      change["isActive"] === false &&
      !window.confirm(
        `Deactivate ${user.name}? Their existing tickets will remain in the workspace.`,
      )
    )
      return;
    const request = this.requests.begin("mutation");
    this.saving.set(true);
    try {
      await this.api.patch(`/users/${user.id}`, change);
      if (!this.requests.isCurrent(request)) return;
      if (
        user.id === this.user()?.id &&
        (change["role"] || change["isActive"] === false)
      ) {
        this.onSessionExpired();
        this.loginError = "Your account access changed. Please sign in again.";
        return;
      }
      await this.loadPeople();
      if (this.requests.isCurrent(request)) this.notify("Team member updated.");
    } catch (error) {
      if (!this.requests.isCurrent(request)) return;
      this.notify(errorMessage(error), true);
      if (this.user()) await this.loadPeople();
    } finally {
      if (this.requests.isCurrent(request)) this.saving.set(false);
    }
  }
  async saveSettings(): Promise<void> {
    if (!this.settingsForm || this.saving()) return;
    const request = this.requests.begin("mutation");
    this.saving.set(true);
    try {
      const settings = await this.api.put<Settings>(
        "/settings",
        this.settingsForm,
      );
      if (!this.requests.isCurrent(request)) return;
      if (this.page() === "settings") {
        this.requests.begin("page");
        this.loading.set(false);
        this.settingsForm = settings;
      }
      const current = this.user();
      if (current)
        this.user.set({
          ...current,
          organizationName: settings.organizationName,
        });
      this.notify(
        "Workspace settings saved. New SLA policies apply to future tickets.",
      );
    } catch (error) {
      if (this.requests.isCurrent(request))
        this.notify(errorMessage(error), true);
    } finally {
      if (this.requests.isCurrent(request)) this.saving.set(false);
    }
  }
  openNotifications(): void {
    this.notificationsDialog?.nativeElement.showModal();
  }
  async readNotifications(notification?: Notification): Promise<void> {
    if (!this.user()) return;
    const request = this.requests.begin("notificationMutation");
    const ids = notification
      ? [notification.id]
      : this.notifications()
          .filter((n) => !n.isRead)
          .map((n) => n.id);
    if (!ids.length) return;
    try {
      await this.api.post("/notifications/read", { ids });
      if (this.requests.isCurrent(request)) await this.loadNotifications();
    } catch (error) {
      if (this.requests.isCurrent(request))
        this.notify(errorMessage(error), true);
    }
  }
  notificationClick(notification: Notification): void {
    void this.readNotifications(notification);
    if (notification.ticketId) {
      this.notificationsDialog?.nativeElement.close();
      this.openTicket(notification.ticketId);
    }
  }
  async exportReport(): Promise<void> {
    if (!this.user()) return;
    const request = this.requests.begin("export");
    try {
      await this.api.download(this.days, () =>
        this.requests.isCurrent(request),
      );
      if (this.requests.isCurrent(request))
        this.notify("Your report was downloaded.");
    } catch (error) {
      if (this.requests.isCurrent(request))
        this.notify(errorMessage(error), true);
    }
  }
  notify(message: string, error = false): void {
    clearTimeout(this.successTimer);
    this.toast.set(message);
    this.toastError.set(error);
    if (!error) this.successTimer = setTimeout(() => this.toast.set(""), 6000);
  }
  pageTitle(): string {
    return {
      overview: "Overview",
      tickets: this.staff() ? "All tickets" : "My tickets",
      detail: "Ticket details",
      customers: "Customers",
      team: "Team members",
      reports: "Reports",
      audit: "Audit history",
      settings: "Workspace settings",
    }[this.page()];
  }
  chartLabel(date: string): string {
    return new Date(date).toLocaleDateString("en", {
      month: "short",
      day: "numeric",
      timeZone: "UTC",
    });
  }
  teamPercent(count: number): number {
    return Math.max(
      0,
      Math.min(
        100,
        (count /
          Math.max(
            1,
            ...(this.dashboard()?.team.map((t) => t.activeTickets) ?? [1]),
          )) *
          100,
      ),
    );
  }
}
