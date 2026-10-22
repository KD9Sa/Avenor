import { bootstrapApplication } from "@angular/platform-browser";
import {
  provideHttpClient,
  withInterceptors,
  withXsrfConfiguration,
} from "@angular/common/http";
import { AppComponent } from "./app/app.component";
import { sessionInterceptor } from "./app/api.service";

bootstrapApplication(AppComponent, {
  providers: [
    provideHttpClient(
      withInterceptors([sessionInterceptor]),
      withXsrfConfiguration({
        cookieName: "XSRF-TOKEN",
        headerName: "X-XSRF-TOKEN",
      }),
    ),
  ],
}).catch((error) => console.error(error));
