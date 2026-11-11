import { Component, signal, computed, inject, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, takeUntil } from 'rxjs';
import { AskApiService, ApiError } from '../../core/api/ask-api.service';
import { AskResponse, RefusalResponse } from '../../core/api/models';
import { ResultTableComponent } from './components/result-table';
import { SqlViewerComponent } from './components/sql-viewer';
import { AdhocWarningComponent } from './components/adhoc-warning';
import { RefusalMessageComponent } from './components/refusal-message';
import { ResultChartComponent } from './components/result-chart';

export interface Turn {
  question: string;
  response: AskResponse | RefusalResponse | ApiError | null;
  isLoading: boolean;
  error?: string;
  warningMessages: string[];
}

@Component({
  selector: 'app-chat-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ResultTableComponent,
    SqlViewerComponent,
    AdhocWarningComponent,
    RefusalMessageComponent,
    ResultChartComponent,
  ],
  templateUrl: './chat-page.html',
  styleUrl: './chat-page.css',
})
export class ChatPageComponent implements OnDestroy {
  private readonly api = inject(AskApiService);
  private readonly destroy$ = new Subject<void>();

  turns = signal<Turn[]>([]);
  conversationId = signal<string | null>(null);
  question = signal('');
  isLoading = signal(false);
  error = signal<string | null>(null);

  showEmptyState = computed(() => this.turns().length === 0 && !this.isLoading());

  exampleQuestions = [
    'Show total revenue by region',
    'Top 10 products by sales last quarter',
    'Monthly user growth trend',
  ];

  send(): void {
    const q = this.question().trim();
    if (!q || this.isLoading()) return;

    this.isLoading.set(true);
    this.error.set(null);

    const newTurn: Turn = {
      question: q,
      response: null,
      isLoading: true,
      warningMessages: [],
    };
    this.turns.update((t) => [...t, newTurn]);
    this.question.set('');

    this.api
      .ask(q, this.conversationId() || undefined)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (result) => this.handleResult(result),
        error: (err: unknown) => this.handleUnexpectedError(err),
        complete: () => {},
      });
  }

  private handleResult(result: AskResponse | RefusalResponse | ApiError): void {
    if (this.isRefusal(result)) {
      this.handleRefusal(result);
    } else if (this.isApiError(result)) {
      this.handleApiError(result);
    } else {
      this.handleSuccess(result);
    }
  }

  private handleSuccess(response: AskResponse): void {
    this.conversationId.set(response.conversationId);
    this.updateLastTurn({
      response,
      isLoading: false,
      warningMessages: response.warnings ?? [],
    });
  }

  private handleRefusal(response: RefusalResponse): void {
    if (response.conversationId) {
      this.conversationId.set(response.conversationId);
    }
    this.updateLastTurn({
      response,
      isLoading: false,
      warningMessages: [],
    });
  }

  private handleApiError(error: ApiError): void {
    this.updateLastTurn({
      response: error,
      isLoading: false,
      error: error.message,
      warningMessages: [],
    });
  }

  private handleUnexpectedError(err: unknown): void {
    const message = err instanceof Error ? err.message : 'Error inesperado. Inténtalo de nuevo.';
    this.updateLastTurn({
      response: { message, status: 0 } as ApiError,
      isLoading: false,
      error: message,
      warningMessages: [],
    });
  }

  private updateLastTurn(partial: Partial<Turn>): void {
    this.turns.update((turns) => {
      const last = turns[turns.length - 1];
      return [...turns.slice(0, -1), { ...last, ...partial }];
    });
    this.isLoading.set(false);
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  newConversation(): void {
    this.turns.set([]);
    this.conversationId.set(null);
    this.error.set(null);
  }

  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.send();
    }
  }

  onQuestionInput(event: Event): void {
    const target = event.target as HTMLTextAreaElement;
    this.question.set(target.value);
  }

  isRefusal(response: unknown): response is RefusalResponse {
    return (
      response !== null &&
      typeof response === 'object' &&
      'reasonCode' in response &&
      (response as RefusalResponse).reasonCode === 'REFUSED'
    );
  }

  isApiError(response: unknown): response is ApiError {
    return (
      response !== null &&
      typeof response === 'object' &&
      'status' in response &&
      'message' in response &&
      !this.isRefusal(response)
    );
  }

  isSuccess(response: unknown): response is AskResponse {
    return (
      response !== null &&
      typeof response === 'object' &&
      'sql' in response &&
      'result' in response
    );
  }
}