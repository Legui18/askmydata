import { Component, input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-adhoc-warning',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './adhoc-warning.html',
  styleUrl: './adhoc-warning.css',
})
export class AdhocWarningComponent {
  warnings = input<string[]>([]);
}