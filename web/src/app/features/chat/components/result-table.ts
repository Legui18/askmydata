import { Component, input } from '@angular/core';
import { CommonModule } from '@angular/common';

export interface Column {
  name: string;
  type: string;
}

@Component({
  selector: 'app-result-table',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './result-table.html',
  styleUrl: './result-table.css',
})
export class ResultTableComponent {
  columns = input.required<Column[]>();
  rows = input.required<unknown[][]>();
  truncated = input<boolean>(false);

  private readonly numericTypes = new Set([
    'int', 'integer', 'bigint', 'smallint', 'tinyint',
    'decimal', 'numeric', 'float', 'real', 'double', 'money', 'smallmoney',
    'number'
  ]);

  private readonly dateTypes = new Set([
    'date', 'datetime', 'datetime2', 'smalldatetime', 'timestamp'
  ]);

  isNumericColumn(col: Column): boolean {
    const type = col.type.toLowerCase();
    return this.numericTypes.has(type);
  }

  isDateColumn(col: Column): boolean {
    const type = col.type.toLowerCase();
    return this.dateTypes.has(type);
  }

  formatCell(cell: unknown, col: Column): string {
    if (cell === null || cell === undefined) return '';
    if (this.isNumericColumn(col) && typeof cell === 'number') {
      return this.formatNumber(cell);
    }
    if (this.isDateColumn(col) && typeof cell === 'string') {
      return this.formatDateForDisplay(cell);
    }
    return String(cell);
  }

  /** Formatea fecha: si es "YYYY-MM-DDT00:00:00" -> "YYYY-MM-DD", si no deja completa */
  private formatDateForDisplay(value: string): string {
    const match = value.match(/^(\d{4}-\d{2}-\d{2})T00:00:00/);
    if (match) return match[1];
    return value;
  }

  private formatNumber(value: number): string {
    const abs = Math.abs(value);
    const maxDecimals = abs !== 0 && abs < 1 ? 4 : 2;
    return new Intl.NumberFormat(undefined, {
      maximumFractionDigits: maxDecimals,
      useGrouping: true,
    }).format(value);
  }

  getCellAlign(col: Column): 'right' | 'left' {
    return this.isNumericColumn(col) ? 'right' : 'left';
  }
}