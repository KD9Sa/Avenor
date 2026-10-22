# Avenor · a clearer way to care

Established before implementation, 22 October 2026.

## Identity

Avenor is an original coined product name inspired by an avenue forward: a clear path from a customer's question to a helpful answer. The identity pairs a geometric open avenue mark with precise, approachable typography. Its voice is calm, capable and human. The portfolio demo organization is Northstar Studio; a second organization demonstrates tenant isolation.

## Visual direction

A quiet editorial workspace: deep navy navigation, a warm white canvas, slim borders, generous space and restrained cobalt actions. Typography and alignment carry the hierarchy. Avoid gradients, rainbow KPI cards, oversized rounded panels and ornamental graphics. Use a small amount of pale blue and amber to communicate urgency. The dashboard should feel like a premium operational tool with a warm greeting, compact metrics, a genuine ticket-volume chart, prioritized queue and useful team workload.

## Palette

| Token | HEX | Purpose |
|---|---|---|
| navy | #13243C | Navigation and brand foundation |
| primary | #3659E3 | Main actions and selected controls |
| primary-hover | #2947C4 | Action hover |
| secondary | #7386A3 | Supporting visualization |
| accent | #C58A39 | Warm highlights |
| background | #F6F7F9 | Workspace canvas |
| surface | #FFFFFF | Panels, forms and dialogs |
| text | #1C2940 | Main text |
| muted | #637086 | Supporting text; AA on both surface and canvas |
| border | #E5E9F0 | Dividers and input boundaries |
| info / tint | #3659E3 / #EEF2FF | Open/informational |
| success / tint | #187653 / #EAF5EF | Resolution and healthy SLA |
| warning / tint | #94611D / #FFF4DF | Pending and approaching deadline |
| danger / tint | #B84146 / #FCEEF0 | Overdue and validation errors |

Text and essential icons must meet WCAG AA contrast. Charts and statuses also use labels/patterns, never color alone.

## Typography and tokens

Use a locally available system UI sans stack (Inter when installed, Segoe UI otherwise), avoiding a network font dependency. Display 30/38 px weight 650; page title 26/34 weight 650; section 16/24 weight 600; body 14/22 weight 400; labels 12/18 weight 500; data 13/20. Use tabular numerals for metrics and tickets. Letter spacing -0.025em for titles and 0.06em for uppercase eyebrow labels.

Spacing: 4, 8, 12, 16, 20, 24, 32, 40, 48 px. Radius: 6 px controls, 10 px cards, 14 px dialogs, 999 px badges/avatars. Shadows: subtle 0 2px 8px #13243C08 on panels; 0 20px 80px #13243C26 on dialogs. Border 1 px. Navigation 224 px. Content maximum 1560 px. Breakpoints 640, 960, 1280 px; collapse navigation below 960 px and stack panels below 640 px. Tables scroll within their own container.

Use one consistent family of 18–20 px line icons, 1.7 px stroke. Brand mark: two converging avenue strokes with a short crossbar, contained in a square. Favicon reuses the mark.

## Components and behavior

Primary filled cobalt, secondary white bordered, quiet text actions, destructive red. Buttons have hover/focus/disabled/loading states. Inputs have persistent labels, descriptive errors and native autocomplete. Focus: 3 px cobalt translucent outline with 2 px offset. Minimum touch target 40 px. Cards use restrained borders. Tables have clear headers, row hover and sortable/filterable data. Badges combine a word with a colored dot. Dialogs have a title, close action, focus management, Escape handling and scroll containment. Toasts use live regions and persist errors until dismissed. Confirmation is required only for destructive changes.

## Screens and completeness

Login; overview; searchable and paginated ticket queue; create-ticket dialog; ticket conversation with public replies/internal notes and assignment/status/priority updates; customer directory; team administration; reports; audit history; workspace/SLA settings; notification center; profile and logout. Customer accounts see their own queue and public replies. Agent/admin navigation reflects permissions. Date-range controls recalculate real persisted metrics. CSV export uses the current dataset. Demonstration data is clearly labeled and resets only via an explicit development command.
