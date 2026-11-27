import { CartesianGrid, Line, LineChart, XAxis } from 'recharts'

const activity = [
  { day: 'Mon', count: 16 }, { day: 'Tue', count: 26 }, { day: 'Wed', count: 22 },
  { day: 'Thu', count: 39 }, { day: 'Fri', count: 35 }, { day: 'Sat', count: 53 }, { day: 'Sun', count: 48 },
]

export default function WorkspaceChart() {
  return <LineChart responsive width="100%" height={91} data={activity} accessibilityLayer={false} aria-hidden="true" margin={{ top: 6, right: 8, left: 8, bottom: 0 }}>
    <CartesianGrid vertical={false} stroke="#e9eef5" />
    <XAxis dataKey="day" interval={0} padding={{ left: 8, right: 8 }} tick={{ fontSize: 8, fill: '#64748b' }} tickLine={false} axisLine={false} height={17} />
    <Line type="monotone" dataKey="count" stroke="#3f5ef7" strokeWidth={2} dot={false} isAnimationActive={false} />
  </LineChart>
}
