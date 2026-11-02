// Display formatting only: never use these strings for calculations, IDs or years.
const countFormatter = new Intl.NumberFormat('en-US', { maximumFractionDigits: 0 })
const metricFormatter = new Intl.NumberFormat('en-US', { maximumFractionDigits: 20 })

export const formatCount = (value: number) => countFormatter.format(Math.round(value))
export const formatMetric = (value: number) => metricFormatter.format(value)

// Recharts may supply text, missing values or arrays as well as numbers.
export function formatChartValue(value: unknown): string {
  if (Array.isArray(value)) return value.map(formatChartValue).join(' – ')
  return typeof value === 'number' ? formatMetric(value) : value == null ? '—' : String(value)
}
