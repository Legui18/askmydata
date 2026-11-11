import { Component, input, viewChild, ElementRef, effect, OnDestroy, signal, afterNextRender } from '@angular/core';
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

  canvasRef = viewChild<ElementRef<HTMLCanvasElement>>('chartCanvas');
  chart = signal<Chart | null>(null);
  shouldShowChart = signal(false);

  private prevSuggestion: ChartSuggestion | null = null;
  private prevColumns: Column[] | null = null;
  private prevRows: unknown[][] | null = null;
  private hasRendered = false;
  private themeMediaQuery: MediaQueryList | null = null;
  private themeListener: ((event: MediaQueryListEvent) => void) | null = null;

  // Paleta sobria: azul, verde, violeta, ámbar, naranja (para eje derecho)
  private readonly palette = [
    { rgb: '59, 130, 246', hex: '#3b82f6' },    // azul
    { rgb: '16, 185, 129', hex: '#10b981' },    // verde
    { rgb: '139, 92, 246', hex: '#8b5cf6' },    // violeta
    { rgb: '245, 158, 11', hex: '#f59e0b' },    // ámbar
  ];
  private readonly rightAxisColor = { rgb: '234, 88, 12', hex: '#ea580c' }; // naranja

  constructor() {
    afterNextRender(() => {
      const canvasEl = this.canvasRef();
      if (canvasEl && this.shouldShowChart() && !this.hasRendered) {
        const suggestion = this.chartSuggestion();
        const cols = this.columns();
        const data = this.rows();
        if (suggestion && cols.length > 0 && data.length >= 2) {
          this.renderChart(suggestion, cols, data);
          this.hasRendered = true;
        }
      }
    });

    if (typeof window !== 'undefined' && window.matchMedia) {
      this.themeMediaQuery = window.matchMedia('(prefers-color-scheme: dark)');
      this.themeListener = () => this.onThemeChange();
      this.themeMediaQuery.addEventListener('change', this.themeListener);
    }

    effect(() => {
      const canvasEl = this.canvasRef();
      const suggestion = this.chartSuggestion();
      const cols = this.columns();
      const data = this.rows();

      const shouldRender =
        suggestion &&
        (suggestion.type === 'bar' || suggestion.type === 'line') &&
        cols.length > 0 &&
        data.length >= 2 &&
        (suggestion !== this.prevSuggestion ||
          cols !== this.prevColumns ||
          data !== this.prevRows);

      if (shouldRender) {
        this.prevSuggestion = suggestion;
        this.prevColumns = cols;
        this.prevRows = data;
        this.shouldShowChart.set(true);

        if (canvasEl) {
          this.renderChart(suggestion, cols, data);
          this.hasRendered = true;
        }
      } else if (!suggestion || suggestion.type === 'table-only' || cols.length === 0 || data.length < 2) {
        this.prevSuggestion = null;
        this.prevColumns = null;
        this.prevRows = null;
        this.shouldShowChart.set(false);
        this.hasRendered = false;
        this.destroyChart();
      }
    });
  }

  ngOnDestroy(): void {
    this.destroyChart();
    if (this.themeMediaQuery && this.themeListener) {
      this.themeMediaQuery.removeEventListener('change', this.themeListener);
      this.themeMediaQuery = null;
      this.themeListener = null;
    }
  }

  private onThemeChange(): void {
    const canvasEl = this.canvasRef();
    const suggestion = this.chartSuggestion();
    const cols = this.columns();
    const data = this.rows();
    if (canvasEl && suggestion && cols.length > 0 && data.length >= 2) {
      this.renderChart(suggestion, cols, data);
    }
  }

  private destroyChart(): void {
    const existing = this.chart();
    if (existing) {
      existing.destroy();
      this.chart.set(null);
    }
  }

  /** Lee las variables CSS actuales y devuelve un objeto con los colores resueltos */
  private getThemeColors(): {
    textSecondary: string;
    textMuted: string;
    borderColor: string;
    surfaceColor: string;
    textPrimary: string;
  } {
    const root = document.documentElement;
    const style = getComputedStyle(root);
    return {
      textSecondary: style.getPropertyValue('--text-secondary').trim() || '#525252',
      textMuted: style.getPropertyValue('--text-muted').trim() || '#a3a3a3',
      borderColor: style.getPropertyValue('--border-color').trim() || '#e5e5e5',
      surfaceColor: style.getPropertyValue('--surface-color').trim() || '#ffffff',
      textPrimary: style.getPropertyValue('--text-primary').trim() || '#171717',
    };
  }

  private renderChart(suggestion: ChartSuggestion, cols: Column[], rows: unknown[][]): void {
    const canvasEl = this.canvasRef();
    if (!canvasEl) return;

    this.destroyChart();

    const canvas = canvasEl.nativeElement;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const xIndex = cols.findIndex((c) => c.name === suggestion.xColumn);
    if (xIndex === -1) {
      this.shouldShowChart.set(false);
      return;
    }

    const yIndices = suggestion.yColumns
      .map((name: string) => cols.findIndex((c) => c.name === name))
      .filter((i: number) => i !== -1);

    if (yIndices.length === 0) {
      this.shouldShowChart.set(false);
      return;
    }

    const xValues = rows.map((r) => r[xIndex] ?? '');

    // Colores del tema actual
    const theme = this.getThemeColors();

    // Determinar qué series van al eje derecho
    const seriesInfo = yIndices.map((yIndex: number, idx: number) => {
      const col = cols[yIndex];
      const values = rows.map((r) => Number(r[yIndex] ?? 0));
      const isRightAxis = this.shouldUseRightAxis(values, yIndices[0], rows, col.name);
      return { yIndex, col, values, isRightAxis, idx };
    });

    const hasRightAxis = seriesInfo.some(s => s.isRightAxis);

    const datasets = seriesInfo.map(({ yIndex, col, values, isRightAxis, idx }) => {
      // Elegir color: naranja si va al eje derecho, sino siguiente de la paleta
      const color = isRightAxis ? this.rightAxisColor : this.palette[idx % this.palette.length];
      return {
        label: col.name,
        data: values,
        borderColor: color.hex,
        backgroundColor: `${color.hex}99`, // 60% opacity (0.6 * 255 ≈ 153 = 0x99)
        borderWidth: 2,
        yAxisID: isRightAxis ? 'y1' : 'y',
        fill: suggestion.type === 'line' ? false : undefined,
        tension: suggestion.type === 'line' ? 0.3 : undefined,
      };
    });

    const scales: NonNullable<ChartConfiguration['options']>['scales'] = {
      x: {
        grid: { display: false },
        ticks: { color: theme.textMuted, font: { family: 'var(--font-sans)' } },
      },
      y: {
        type: 'linear',
        display: true,
        position: 'left',
        grid: { color: theme.borderColor },
        ticks: {
          color: theme.textMuted,
          font: { family: 'var(--font-sans)' },
          callback: (value: number | string) => this.formatNumber(value as number),
        },
      },
    };

    if (hasRightAxis) {
      scales['y1'] = {
        type: 'linear',
        display: true,
        position: 'right',
        grid: { drawOnChartArea: false },
        ticks: {
          color: this.rightAxisColor.hex, // naranja para textos del eje derecho
          font: { family: 'var(--font-sans)' },
          callback: (value: number | string) => this.formatNumber(value as number),
        },
      };
    }

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
          legend: { display: datasets.length > 1, position: 'top' as const, labels: { color: theme.textSecondary, font: { family: 'var(--font-sans)' } } },
          tooltip: {
            enabled: true,
            backgroundColor: theme.surfaceColor,
            titleColor: theme.textPrimary,
            bodyColor: theme.textSecondary,
            borderColor: theme.borderColor,
            borderWidth: 1,
            padding: 12,
            callbacks: {
              label: (context) => {
                const label = context.dataset.label || '';
                const value = context.parsed.y as number;
                return `${label}: ${this.formatNumber(value)}`;
              },
            },
          },
        },
        scales,
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
    const firstValues = rows.map((r) => Number(r[firstYIndex] ?? 0));
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

  private formatNumber(value: number): string {
    const abs = Math.abs(value);
    const maxDecimals = abs !== 0 && abs < 1 ? 4 : 2;
    return new Intl.NumberFormat(undefined, {
      maximumFractionDigits: maxDecimals,
      useGrouping: true,
    }).format(value);
  }
}