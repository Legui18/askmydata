export interface Column {
  name: string;
  type: string;
}

export interface Result {
  columns: Column[];
  rows: unknown[][];
}

export interface ChartSuggestion {
  type: 'bar' | 'line' | 'table-only';
  xColumn: string;
  yColumns: string[];
}

export interface Usage {
  promptTokens: number;
  completionTokens: number;
  totalTokens: number;
  estimatedCostUsd: number;
  elapsedMs: number;
}

export interface AskResponse {
  conversationId: string;
  sql: string;
  result: Result;
  truncated: boolean;
  chartSuggestion: ChartSuggestion | null;
  explanation: string;
  metricUsage: string[];
  warnings: string[];
  usage: Usage;
}

export interface RefusalResponse {
  title: string;
  status: number;
  reasonCode: 'REFUSED';
  refusal: string;
  conversationId: string | null;
  correlationId: string;
  usage: Usage;
}

export type AskApiResult = AskResponse | RefusalResponse;