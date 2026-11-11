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
}