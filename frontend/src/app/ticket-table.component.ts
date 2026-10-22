import { Component, EventEmitter, Input, Output } from "@angular/core";
import { DatePipe } from "@angular/common";
import { IconComponent } from "./icon.component";
import { Ticket } from "./models";
import { initials, relativeTime, statusLabel } from "./helpers";

@Component({
  selector: "av-ticket-table",
  standalone: true,
  imports: [IconComponent, DatePipe],
  template: ` <div class="table-wrap">
    <table class="ticket-table">
      <thead>
        <tr>
          <th>Ticket</th>
          <th>Customer</th>
          <th>Status</th>
          <th>Priority</th>
          <th>Assignee</th>
          <th>Updated</th>
        </tr>
      </thead>
      <tbody>
        @for (ticket of tickets; track ticket.id) {
          <tr>
            <td>
              <a
                [href]="'#/tickets/' + ticket.id"
                class="ticket-title"
                (click)="open.emit(ticket.id); $event.preventDefault()"
                >{{ ticket.subject }}</a
              ><span class="table-sub"
                >#AV-{{ ticket.number }} <span class="table-dot">·</span>
                {{ ticket.category }}</span
              >
            </td>
            <td>
              <div class="person">
                <span
                  class="avatar small"
                  [class]="
                    'avatar small avatar-' +
                    (ticket.customerName.charCodeAt(0) % 5)
                  "
                  >{{ initials(ticket.customerName) }}</span
                ><span>{{ ticket.customerName }}</span>
              </div>
            </td>
            <td>
              <span [class]="'badge status-' + ticket.status"
                ><span class="status-dot"></span
                >{{ label(ticket.status) }}</span
              >
            </td>
            <td>
              <span [class]="'priority priority-' + ticket.priority"
                ><span class="priority-bars">▂▅▇</span
                >{{ ticket.priority }}</span
              >
            </td>
            <td>
              @if (ticket.assigneeName) {
                <span class="assignee"
                  ><span class="avatar tiny">{{
                    initials(ticket.assigneeName)
                  }}</span
                  >{{ ticket.assigneeName.split(" ")[0] }}</span
                >
              } @else {
                <span class="muted">Unassigned</span>
              }
            </td>
            <td class="muted" [title]="ticket.updatedAt | date: 'medium'">
              {{ relative(ticket.updatedAt) }}
            </td>
          </tr>
        } @empty {
          <tr>
            <td colspan="6">
              <div class="empty-state compact">
                <av-icon name="inbox" /><strong>No tickets here yet</strong
                ><span
                  >Try another filter, or create a ticket to get started.</span
                >
              </div>
            </td>
          </tr>
        }
      </tbody>
    </table>
  </div>`,
  styles: [":host{display:block}"],
})
export class TicketTableComponent {
  @Input() tickets: Ticket[] = [];
  @Output() open = new EventEmitter<string>();
  readonly initials = initials;
  readonly label = statusLabel;
  readonly relative = relativeTime;
}
