import { Component, input, signal } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-sql-viewer',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './sql-viewer.html',
  styleUrl: './sql-viewer.css',
})
export class SqlViewerComponent {
  sql = input.required<string>();
  expanded = signal(false);
  copied = signal(false);

  toggle(): void {
    this.expanded.update((v) => !v);
  }

  async copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.sql());
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2000);
    } catch {
      // Silencioso: clipboard puede fallar en contextos inseguros
    }
  }
}