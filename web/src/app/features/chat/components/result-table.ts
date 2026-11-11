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
      return new Intl.NumberFormat(undefined, {
        maximumFractionDigits: 2,
        useGrouping: true,
      }).format(cell);
    }
    return String(cell);
  }

  getCellAlign(col: Column): 'right' | 'left' {
    return this.isNumericColumn(col) ? 'right' : 'left';
  }
}