import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import { AskResponse, RefusalResponse, AskApiResult } from './models';

export interface ApiError {
  message: string;
  status: number;
}

@Injectable({ providedIn: 'root' })
export class AskApiService {
  private readonly http = inject(HttpClient);
  private readonly endpoint = '/api/ask';

  ask(question: string, conversationId?: string): Observable<AskApiResult | ApiError> {
    const body = conversationId ? { question, conversationId } : { question };

    return this.http.post<AskResponse | RefusalResponse>(this.endpoint, body).pipe(
      map((response) => response as AskApiResult),
      catchError((error: HttpErrorResponse) => {
        if (error.status === 422) {
          return throwError(() => error.error as RefusalResponse);
        }
        const apiError: ApiError = {
          message: this.getErrorMessage(error),
          status: error.status,
        };
        return throwError(() => apiError);
      })
    );
  }

  private getErrorMessage(error: HttpErrorResponse): string {
    if (error.status === 0) {
      return 'No se puede conectar con el servidor. Verifica que el backend esté en ejecución.';
    }
    if (error.status >= 500) {
      return 'Error interno del servidor. Inténtalo de nuevo más tarde.';
    }
    return `Error ${error.status}: ${error.message}`;
  }
}