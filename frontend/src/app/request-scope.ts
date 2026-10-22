export interface RequestToken {
  readonly session: number;
  readonly lane: string;
  readonly request: number;
}

/** A response may update the workspace only in its session and while it is latest. */
export class RequestScope {
  private generation = 0;
  private readonly lanes = new Map<string, number>();

  get session(): number {
    return this.generation;
  }

  begin(lane: string): RequestToken {
    const request = (this.lanes.get(lane) ?? 0) + 1;
    this.lanes.set(lane, request);
    return { session: this.generation, lane, request };
  }

  isCurrent(token: RequestToken): boolean {
    return (
      token.session === this.generation &&
      token.request === this.lanes.get(token.lane)
    );
  }

  invalidate(): void {
    this.generation++;
    this.lanes.clear();
  }
}

// One browser workspace shares the epoch with the HTTP session interceptor.
export const sessionRequests = new RequestScope();
