import { Component, input, viewChild, ElementRef, effect, OnDestroy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Chart, ChartConfiguration, ChartType, registerables } from 'chart.js';
import { ChartSuggestion } from '../../../core/api/models';

Chart.register(...registerables);

export interface Column {
  name: string;
  type: string;
}

@Component({
  selector: 'app-result-chart',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './result-chart.html',
  styleUrl: './result-chart.css',
})
export class ResultChartComponent implements OnDestroy {
  columns = input.required<Column[]>();
  rows = input.required<unknown[][]>();
  chartSuggestion = input<ChartSuggestion | null>(null);

  canvasRef = viewChild.required<ElementRef<HTMLCanvasElement>>('chartCanvas');
  chart = signal<Chart | null>(null);

  constructor() {
    effect(() => {
      const suggestion = this.chartSuggestion();
      const cols = this.columns();
      const data = this.rows();

      if (suggestion && suggestion.type !== 'table-only' && cols.length > 0 && data.length > 0) {
        this.renderChart(suggestion, cols, data);
      } else {
        this.destroyChart();
      }
    });
  }

  ngOnDestroy(): void {
    this.destroyChart();
  }

  private destroyChart(): void {
    const existing = this.chart();
    if (existing) {
      existing.destroy();
      this.chart.set(null);
    }
  }

  private renderChart(suggestion: ChartSuggestion, cols: Column[], rows: unknown[][]): void {
    this.destroyChart();

    const canvas = this.canvasRef().nativeElement;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const xIndex = cols.findIndex((c) => c.name === suggestion.xColumn);
    if (xIndex === -1) return;

    const yIndices = suggestion.yColumns
      .map((name: string) => cols.findIndex((c) => c.name === name))
      .filter((i: number) => i !== -1);

    if (yIndices.length === 0) return;

    const xValues = rows.map((r) => r[xIndex] ?? '');

    const datasets = yIndices.map((yIndex: number, idx: number) => {
      const col = cols[yIndex];
      const values = rows.map((r) => (r[yIndex] as number) ?? 0);
      const isRightAxis = this.shouldUseRightAxis(values, yIndices[0], rows, col.name);

      return {
        label: col.name,
        data: values,
        borderColor: this.getColor(idx, isRightAxis),
        backgroundColor: this.getColor(idx, isRightAxis, true),
        yAxisID: isRightAxis ? 'y1' : 'y',
        fill: suggestion.type === 'line' ? false : undefined,
        tension: suggestion.type === 'line' ? 0.3 : undefined,
      };
    });

    const config: ChartConfiguration = {
      type: suggestion.type as ChartType,
      data: {
        labels: xValues,
        datasets,
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        interaction: { mode: 'index', intersect: false },
        plugins: {
          legend: { display: datasets.length > 1, position: 'top' as const },
          tooltip: { enabled: true },
        },
        scales: {
          x: {
            grid: { display: false },
            ticks: { color: 'var(--text-muted)', font: { family: 'var(--font-sans)' } },
          },
          y: {
            type: 'linear',
            display: true,
            position: 'left',
            grid: { color: 'var(--border-color)' },
            ticks: { color: 'var(--text-muted)', font: { family: 'var(--font-sans)' } },
          },
          y1: {
            type: 'linear',
            display: true,
            position: 'right',
            grid: { drawOnChartArea: false },
            ticks: { color: 'var(--text-muted)', font: { family: 'var(--font-sans)' } },
          },
        },
      },
    };

    this.chart.set(new Chart(ctx, config));
  }

  /**
   * Decide si una serie debe ir al eje Y derecho (segundo eje).
   * Criterio simple:
   * - Si el máximo de la serie es más de 50 veces el máximo de la primera serie Y
   * - O si el nombre de la columna sugiere porcentaje/ratio (contiene %, pct, ratio, rate, porcentaje)
   */
  private shouldUseRightAxis(
    values: number[],
    firstYIndex: number,
    rows: unknown[][],
    columnName: string
  ): boolean {
    const firstValues = rows.map((r) => (r[firstYIndex] as number) ?? 0);
    const firstMax = Math.max(...firstValues.filter((v) => v > 0), 1);
    const thisMax = Math.max(...values.filter((v) => v > 0), 1);

    const ratio = thisMax / firstMax;
    if (ratio > 50 || 1 / ratio > 50) return true;

    const lower = columnName.toLowerCase();
    return (
      lower.includes('%') ||
      lower.includes('pct') ||
      lower.includes('ratio') ||
      lower.includes('rate') ||
      lower.includes('porcentaje') ||
      lower.includes('porciento')
    );
  }

  private getColor(index: number, isRightAxis: boolean, transparent = false): string {
    const accent = '59, 130, 246'; // azul #3b82f6
    const accentRight = '234, 88, 12'; // naranja suave #ea580c para eje derecho

    const rgb = isRightAxis ? accentRight : accent;
    const alpha = transparent ? 0.15 : 1;

    // Variantes sobrias: acento principal + variantes para múltiples series
    const variants = [
      `rgba(${rgb}, ${alpha})`,
      `rgba(16, 185, 129, ${alpha})`, // verde esmeralda
      `rgba(168, 85, 247, ${alpha})`, // violeta
      `rgba(234, 179, 8, ${alpha})`, // ámbar
    ];

    return variants[index % variants.length];
  }
}