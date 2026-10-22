import { Component, Input } from "@angular/core";

@Component({
  selector: "av-icon",
  standalone: true,
  template: `<svg
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    stroke-width="1.7"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
  >
    <path [attr.d]="paths[name] || paths['circle']" />
  </svg>`,
  styles: [
    ":host{display:inline-flex;width:20px;height:20px;flex:0 0 auto}svg{width:100%;height:100%}",
  ],
})
export class IconComponent {
  @Input() name = "circle";
  readonly paths: Record<string, string> = {
    overview: "M3 3h7v7H3z M14 3h7v7h-7z M3 14h7v7H3z M14 14h7v7h-7z",
    calendar: "M4 5h16v16H4z M8 3v4m8-4v4M4 10h16",
    ticket:
      "M4 4h16v5a3 3 0 0 0 0 6v5H4v-5a3 3 0 0 0 0-6V4z M9 4v3m0 4v2m0 4v3",
    customers:
      "M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2 M12 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0 M17 4a4 4 0 0 1 0 8 M22 21v-2a4 4 0 0 0-3-3.87",
    team: "M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2 M12 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0 M19 8v6m-3-3h6",
    reports: "M3 3v18h18 M7 15v-4m5 4V7m5 8v-6",
    audit: "M12 8v4l3 2 M3.5 6.5A9 9 0 1 1 3 16 M3 3v5h5",
    settings:
      "M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8 M9 3h6l1 3 3 1 2 5-2 5-3 1-1 3H9l-1-3-3-1-2-5 2-5 3-1z",
    search: "M21 21l-5-5 M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0",
    plus: "M12 5v14 M5 12h14",
    arrow: "M5 12h14 M13 6l6 6-6 6",
    chevron: "m9 5 7 7-7 7",
    down: "m6 9 6 6 6-6",
    back: "M19 12H5m6 6-6-6 6-6",
    bell: "M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9 M10 21h4",
    clock: "M12 8v4l3 2 M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0",
    check: "m5 12 4 4L19 6",
    checkcircle: "m8 12 3 3 5-6 M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0",
    inbox: "M4 4h16l2 13v3H2v-3L4 4z M2 15h6l2 3h4l2-3h6",
    export: "M12 3v12m-5-5 5 5 5-5 M4 15v6h16v-6",
    close: "m6 6 12 12 M6 18 18 6",
    mail: "M3 5h18v14H3z m0 1 9 7 9-7",
    globe:
      "M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0 M3 12h18 M12 3c5 5 5 13 0 18-5-5-5-13 0-18",
    chat: "M21 4H3v13h5l4 4v-4h9V4z M7 9h10m-10 4h6",
    flag: "M5 21V3h14l-3 5 3 5H5",
    filter: "M4 7h16M7 12h10m-7 5h4",
    logout: "M9 4H3v16h6 M9 12h12m-5-5 5 5-5 5",
    shield: "m12 3 8 3v6c0 5-8 9-8 9s-8-4-8-9V6z m-4 9 3 3 5-6",
    sun: "M12 3v2m0 14v2M3 12h2m14 0h2M5.6 5.6 1.4 1.4m10 10 1.4 1.4m0-12.8L17 7M7 17l-1.4 1.4 M16 12a4 4 0 1 1-8 0 4 4 0 0 1 8 0",
    menu: "M4 6h16M4 12h16M4 18h16",
    info: "M12 11v6m0-10v.1 M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0",
    lock: "M6 10h12v11H6z M8 10V6a4 4 0 0 1 8 0v4",
    circle: "M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0",
    more: "M5 12h.1M12 12h.1M19 12h.1",
    tag: "M3 3h8l10 10-8 8L3 11V3z M7 7h.1",
    sparkle: "m12 3 2.5 6.5L21 12l-6.5 2.5L12 21l-2.5-6.5L3 12l6.5-2.5z",
  };
}
