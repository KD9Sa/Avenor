import { Injectable, inject } from "@angular/core";
import {
  HttpClient,
  HttpErrorResponse,
  HttpInterceptorFn,
  HttpParams,
} from "@angular/common/http";
import { firstValueFrom, tap } from "rxjs";
import { sessionRequests } from "./request-scope";

export const sessionInterceptor: HttpInterceptorFn = (request, next) => {
  const session = sessionRequests.session;
  return next(request).pipe(
    tap({
      error: (error: unknown) => {
        if (
          error instanceof HttpErrorResponse &&
          error.status === 401 &&
          session === sessionRequests.session &&
          !["/api/auth/login", "/api/auth/me"].includes(request.url)
        )
          window.dispatchEvent(new Event("avenor-session-expired"));
      },
    }),
  );
};

@Injectable({ providedIn: "root" })
export class ApiService {
  private readonly http = inject(HttpClient);
  get<T>(
    path: string,
    query: Record<string, string | number> = {},
  ): Promise<T> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(query))
      if (value !== "") params = params.set(key, value);
    return firstValueFrom(
      this.http.get<T>(`/api${path}`, { params, withCredentials: true }),
    );
  }
  post<T>(path: string, body: unknown): Promise<T> {
    return firstValueFrom(
      this.http.post<T>(`/api${path}`, body, { withCredentials: true }),
    );
  }
  patch<T>(path: string, body: unknown): Promise<T> {
    return firstValueFrom(
      this.http.patch<T>(`/api${path}`, body, { withCredentials: true }),
    );
  }
  put<T>(path: string, body: unknown): Promise<T> {
    return firstValueFrom(
      this.http.put<T>(`/api${path}`, body, { withCredentials: true }),
    );
  }
  async download(days: number, isCurrent: () => boolean): Promise<void> {
    const blob = await firstValueFrom(
      this.http.get(`/api/reports/export?days=${days}`, {
        responseType: "blob",
        withCredentials: true,
      }),
    );
    if (!isCurrent()) return;
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `avenor-report-${days}days.csv`;
    link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
}
export function errorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0)
      return "We could not reach the server. Check that the API is running, then try again.";
    if (error.status === 401)
      return "Your session has ended. Please sign in again.";
    const payload = error.error as {
      detail?: string;
      title?: string;
      errors?: Record<string, string[]>;
    };
    const validation =
      payload?.errors && Object.values(payload.errors).flat().join(" ");
    return (
      validation ||
      payload?.detail ||
      payload?.title ||
      "That request could not be completed. Please try again."
    );
  }
  return error instanceof Error
    ? error.message
    : "Something went wrong. Please try again.";
}
