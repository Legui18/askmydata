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

  isNumericColumn(col: Column): boolean {
    const type = col.type.toLowerCase();
    return this.numericTypes.has(type);
  }

  formatCell(cell: unknown, col: Column): string {
    if (cell === null || cell === undefined) return '';
    if (this.isNumericColumn(col) && typeof cell === 'number') {
      return this.formatNumber(cell);
    }
    return String(cell);
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