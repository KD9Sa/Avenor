import type { Priority, TicketStatus } from "./models";
export const statuses: TicketStatus[] = [
  "Open",
  "InProgress",
  "Pending",
  "Resolved",
  "Closed",
];
export const priorities: Priority[] = ["Low", "Normal", "High", "Urgent"];
export function initials(name: string): string {
  return name
    .trim()
    .split(/\s+/)
    .slice(0, 2)
    .map((part) => part[0] ?? "")
    .join("")
    .toUpperCase();
}
export function statusLabel(status: string): string {
  return status === "InProgress" ? "In progress" : status;
}
export function allowedTransitions(status: TicketStatus): TicketStatus[] {
  return (
    {
      Open: ["InProgress", "Pending", "Resolved"],
      InProgress: ["Open", "Pending", "Resolved"],
      Pending: ["Open", "InProgress", "Resolved"],
      Resolved: ["Open", "Closed"],
      Closed: [],
    } as Record<TicketStatus, TicketStatus[]>
  )[status];
}
export function responseTime(minutes: number): string {
  return minutes < 60
    ? `${Math.round(minutes)}m`
    : `${Math.floor(minutes / 60)}h ${Math.round(minutes % 60)}m`;
}
export function relativeTime(value: string, now = Date.now()): string {
  const minutes = Math.max(
    0,
    Math.floor((now - new Date(value).getTime()) / 60000),
  );
  return minutes < 1
    ? "Just now"
    : minutes < 60
      ? `${minutes}m ago`
      : minutes < 1440
        ? `${Math.floor(minutes / 60)}h ago`
        : `${Math.floor(minutes / 1440)}d ago`;
}
export function slaState(
  ticket: {
    status: string;
    firstRespondedAt: string | null;
    responseDueAt: string;
    resolutionDueAt: string;
  },
  now = Date.now(),
): { label: string; state: string } {
  if (["Resolved", "Closed"].includes(ticket.status))
    return { label: "Completed", state: "success" };
  const due = new Date(
    ticket.firstRespondedAt ? ticket.resolutionDueAt : ticket.responseDueAt,
  ).getTime();
  const minutes = Math.ceil((due - now) / 60000);
  if (minutes < 0) return { label: "Overdue", state: "danger" };
  if (minutes <= 60) return { label: `${minutes}m left`, state: "warning" };
  return { label: `${Math.floor(minutes / 60)}h left`, state: "muted" };
}
export function validateTicket(
  subject: string,
  description: string,
  isStaff: boolean,
  customerId: string,
): string | null {
  if (subject.trim().length < 5 || subject.trim().length > 180)
    return "Use a subject between 5 and 180 characters.";
  if (description.trim().length < 10 || description.trim().length > 10000)
    return "Describe the issue in 10–10,000 characters.";
  if (isStaff && !customerId) return "Choose the customer who needs help.";
  return null;
}
export function chartPath(
  values: number[],
  width: number,
  height: number,
  maximum: number,
): string {
  if (!values.length) return "";
  const max = Math.max(1, maximum);
  return values
    .map(
      (value, index) =>
        `${index === 0 ? "M" : "L"}${((index / Math.max(1, values.length - 1)) * width).toFixed(1)},${(height - (value / max) * height).toFixed(1)}`,
    )
    .join(" ");
}
