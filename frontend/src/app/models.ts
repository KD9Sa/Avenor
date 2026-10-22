export type Role = "Admin" | "Agent" | "Customer";
export type TicketStatus =
  | "Open"
  | "InProgress"
  | "Pending"
  | "Resolved"
  | "Closed";
export type Priority = "Low" | "Normal" | "High" | "Urgent";
export interface User {
  id: string;
  name: string;
  email: string;
  role: Role;
  tenantId: string;
  organizationName: string;
  organizationSlug: string;
  isActive: boolean;
  avatarColor?: string;
}
export interface Ticket {
  id: string;
  number: number;
  subject: string;
  description: string;
  status: TicketStatus;
  priority: Priority;
  category: string;
  channel: string;
  customerId: string;
  customerName: string;
  customerEmail: string;
  assigneeId: string | null;
  assigneeName: string | null;
  createdAt: string;
  updatedAt: string;
  responseDueAt: string;
  resolutionDueAt: string;
  firstRespondedAt: string | null;
  resolvedAt: string | null;
  isEscalated: boolean;
  version: number;
  tags: string[];
}
export interface Message {
  id: string;
  body: string;
  isInternal: boolean;
  authorId: string;
  authorName: string;
  authorRole: Role;
  createdAt: string;
}
export interface Audit {
  id: string;
  action: string;
  description: string;
  actorName: string;
  createdAt: string;
  ticketId: string | null;
}
export interface TicketDetail {
  ticket: Ticket;
  messages: Message[];
  history: Audit[];
}
export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}
export interface Customer {
  id: string;
  name: string;
  email: string;
  company: string;
  ticketCount: number;
  openTicketCount: number;
}
export interface Dashboard {
  totalTickets: number;
  openTickets: number;
  resolvedTickets: number;
  pendingTickets: number;
  unassignedTickets: number;
  overdueTickets: number;
  slaCompliance: number;
  averageResponseMinutes: number;
  volume: { date: string; created: number; resolved: number }[];
  byStatus: { status: TicketStatus; count: number }[];
  byCategory: { category: string; count: number }[];
  team: {
    id: string;
    name: string;
    role: string;
    activeTickets: number;
    resolvedTickets: number;
  }[];
  recentTickets: Ticket[];
}
export interface Settings {
  organizationName: string;
  organizationSlug: string;
  assignmentStrategy: "Manual" | "LeastBusy" | "RoundRobin";
  slaPolicies: {
    priority: Priority;
    responseMinutes: number;
    resolutionMinutes: number;
  }[];
  businessHours: string;
}
export interface Notification {
  id: string;
  title: string;
  body: string;
  ticketId: string | null;
  isRead: boolean;
  createdAt: string;
}
