import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Observable, catchError, finalize, shareReplay, switchMap, throwError } from 'rxjs';
import { AuthService, AuthResponse } from './auth.service';

// Shared across concurrent requests so a burst of 401s triggers a single refresh.
let refreshing$: Observable<AuthResponse> | null = null;

/**
 * Attaches the bearer token to API requests, and transparently refreshes an expired access
 * token (using the stored refresh token) on a 401, then retries the original request once.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const snackBar = inject(MatSnackBar);
  const isApi = req.url.startsWith('/api');
  const isAuthEndpoint = req.url.includes('/api/auth/');

  const withAuth = (token: string | null) =>
    token && isApi ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

  return next(withAuth(auth.token())).pipe(
    catchError((err) => {
      if (err instanceof HttpErrorResponse && err.status === 429) {
        snackBar.open('Rate limit reached — please slow down.', 'OK', { duration: 4000 });
        return throwError(() => err);
      }

      if (err instanceof HttpErrorResponse && err.status >= 500) {
        snackBar.open('Something went wrong on the server. Please try again.', 'OK', { duration: 5000 });
        return throwError(() => err);
      }

      const canRefresh =
        err instanceof HttpErrorResponse &&
        err.status === 401 &&
        isApi &&
        !isAuthEndpoint &&
        !!localStorage.getItem('ais.refresh');

      if (!canRefresh) {
        return throwError(() => err);
      }

      refreshing$ ??= auth.refresh().pipe(
        shareReplay(1),
        finalize(() => (refreshing$ = null)),
      );

      return refreshing$.pipe(
        switchMap(() => next(withAuth(auth.token()))),
        catchError((refreshErr) => {
          auth.logout();
          return throwError(() => refreshErr);
        }),
      );
    }),
  );
};
