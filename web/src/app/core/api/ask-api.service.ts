import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import { AskResponse, RefusalResponse } from './models';

export interface ApiError {
  message: string;
  status: number;
}

@Injectable({ providedIn: 'root' })
export class AskApiService {
  private readonly http = inject(HttpClient);
  private readonly endpoint = '/api/ask';

  ask(question: string, conversationId?: string): Observable<AskResponse | RefusalResponse | ApiError> {
    const body = conversationId ? { question, conversationId } : { question };

    return this.http.post<AskResponse | RefusalResponse>(this.endpoint, body).pipe(
      map((response) => {
        if ('reasonCode' in response && response.reasonCode === 'REFUSED') {
          return response as RefusalResponse;
        }
        return response as AskResponse;
      }),
      catchError((error: HttpErrorResponse) => {
        if (error.status === 422) {
          const refusal = error.error as RefusalResponse;
          if (refusal?.reasonCode === 'REFUSED') {
            return of(refusal);
          }
        }
        const apiError: ApiError = {
          message: this.getErrorMessage(error),
          status: error.status,
        };
        return of(apiError);
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