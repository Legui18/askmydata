export interface Column {
  name: string;
  type: string;
}

export interface Result {
  columns: Column[];
  rows: unknown[][];
  truncated: boolean;
}

export interface ChartSuggestion {
  type: string;
  xColumn: string;
  yColumns: string[];
}

export interface MetricUsage {
  name: string;
  value: number;
  unit: string;
}

export interface Warning {
  code: string;
  message: string;
}

export interface Usage {
  promptTokens: number;
  completionTokens: number;
  totalTokens: number;
}

export interface AskResponse {
  conversationId: string;
  sql: string;
  result: Result;
  chartSuggestion: ChartSuggestion | null;
  explanation: string | null;
  metricUsage: MetricUsage[];
  warnings: Warning[];
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