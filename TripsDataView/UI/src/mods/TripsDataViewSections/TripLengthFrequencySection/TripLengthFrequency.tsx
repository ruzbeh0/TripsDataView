import React, { FC, useCallback, useMemo, useState } from 'react';
import useDataUpdate from 'mods/use-data-update';
import $Panel from 'mods/panel';

import { Bar } from 'react-chartjs-2';
import {
  Chart as ChartJS,
  CategoryScale,
  LinearScale,
  BarElement,
  Tooltip,
  Legend,
  Title,
} from 'chart.js';

ChartJS.register(CategoryScale, LinearScale, BarElement, Tooltip, Legend, Title);

interface TripLengthBinInfo {
  distanceBin: number; // lower edge in km; 30 means 30+ km
  total: number;
}

interface AggregatedInfo {
  label: string;
  min: number;
  max: number | null;
  total: number;
}

interface TripLengthFrequencyProps {
  onClose: () => void;
}

const commonFont = {
  family: 'Arial, sans-serif',
  size: 18,
  weight: 'normal' as const,
};

const GROUPED_RANGES = [
  { label: '0-2', min: 0, max: 2 },
  { label: '2-5', min: 2, max: 5 },
  { label: '5-10', min: 5, max: 10 },
  { label: '10-20', min: 10, max: 20 },
  { label: '20-30', min: 20, max: 30 },
  { label: '30+', min: 30, max: null },
];

const emptyAggregated = (label: string, min: number, max: number | null): AggregatedInfo => ({
  label,
  min,
  max,
  total: 0,
});

const formatDetailedLabel = (bin: number) => (bin >= 30 ? '30+' : `${bin}-${bin + 1}`);

const groupDetailsByDistanceBin = (details: TripLengthBinInfo[]): AggregatedInfo[] => {
  return details
    .slice()
    .sort((a, b) => a.distanceBin - b.distanceBin)
    .map(info => ({
      label: formatDetailedLabel(info.distanceBin),
      min: info.distanceBin,
      max: info.distanceBin >= 30 ? null : info.distanceBin + 1,
      total: info.total,
    }));
};

const aggregateDataByRanges = (details: TripLengthBinInfo[]): AggregatedInfo[] => {
  const aggregated = GROUPED_RANGES.map(range => emptyAggregated(range.label, range.min, range.max));

  for (const info of details) {
    const idx = GROUPED_RANGES.findIndex(range => {
      if (range.max === null) return info.distanceBin >= range.min;
      return info.distanceBin >= range.min && info.distanceBin < range.max;
    });

    if (idx < 0) continue;

    aggregated[idx].total += info.total;
  }

  return aggregated;
};

const percentileFromBins = (details: TripLengthBinInfo[], percentile: number): string => {
  const total = details.reduce((sum, row) => sum + row.total, 0);
  if (total <= 0) return '0';

  const target = Math.ceil(total * percentile);
  let cumulative = 0;

  const sorted = details.slice().sort((a, b) => a.distanceBin - b.distanceBin);
  for (const row of sorted) {
    cumulative += row.total;
    if (cumulative >= target) {
      if (row.distanceBin >= 30) return '30+';
      return (row.distanceBin + 0.5).toFixed(1);
    }
  }

  return '30+';
};

const averageDistanceFromBins = (details: TripLengthBinInfo[]): string => {
  const total = details.reduce((sum, row) => sum + row.total, 0);
  if (total <= 0) return '0';

  const weighted = details.reduce((sum, row) => {
    const midpoint = row.distanceBin >= 30 ? 30.5 : row.distanceBin + 0.5;
    return sum + row.total * midpoint;
  }, 0);

  return (weighted / total).toFixed(1);
};

const TripLengthFrequencyRow: FC<{ row: AggregatedInfo; index: number }> = ({ row, index }) => (
  <div
    className="labels_L7Q row_S2v"
    style={{
      width: '100%',
      padding: '0.75rem 0',
      backgroundColor: index % 2 === 0 ? 'rgba(255,255,255,0.08)' : 'transparent',
      color: 'white',
    }}
  >
    <div style={{ width: '50%', paddingLeft: '1rem' }}>{row.label}</div>
    <div style={{ width: '50%', textAlign: 'center' }}>{row.total}</div>
  </div>
);

const TripLengthFrequency: FC<TripLengthFrequencyProps> = ({ onClose }) => {
  const [details, setDetails] = useState<TripLengthBinInfo[]>([]);
  const [isGrouped, setIsGrouped] = useState<boolean>(false);
  const [showSummaryStats, setShowSummaryStats] = useState<boolean>(false);

  useDataUpdate('tripLengthFrequencyInfo.tripLengthFrequencyDetails', data => {
    setDetails(Array.isArray(data) ? data : []);
  });

  const panWidth = window.innerWidth * 0.44;
  const panHeight = window.innerHeight * 0.62;

  const totalTrips = useMemo(() => details.reduce((sum, row) => sum + row.total, 0), [details]);

  const detailedRows = useMemo(() => groupDetailsByDistanceBin(details), [details]);
  const groupedRows = useMemo(() => aggregateDataByRanges(details), [details]);
  const rowsToUse = isGrouped ? groupedRows : detailedRows;

  const chartData = useMemo(() => ({
    labels: rowsToUse.map(row => row.label),
    datasets: [
      {
        label: 'Trips',
        data: rowsToUse.map(row => row.total),
        backgroundColor: '#4DA6FF',
      },
    ],
  }), [rowsToUse]);

  const chartOptions = useMemo(() => ({
    indexAxis: 'x' as const,
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
      title: {
        display: true,
        text: 'Trip Length Frequency Distribution',
        color: 'white',
        font: commonFont,
      },
      legend: {
        display: false,
      },
      tooltip: {
        callbacks: {
          title: (items: any[]) => `${items?.[0]?.label ?? ''} km`,
        },
      },
    },
    scales: {
      y: {
        title: {
          display: true,
          text: 'Trips',
          color: 'white',
          font: commonFont,
        },
        ticks: {
          color: 'white',
          font: { ...commonFont, size: commonFont.size - 4 },
        },
        grid: {
          color: 'rgba(255,255,255,0.1)',
        },
      },
      x: {
        title: {
          display: true,
          text: 'Straight-line origin-destination distance (km)',
          color: 'white',
          font: commonFont,
        },
        ticks: {
          color: 'white',
          font: { ...commonFont, size: commonFont.size - 4 },
          maxRotation: 60,
          minRotation: 0,
        },
        grid: {
          color: 'rgba(255,255,255,0.1)',
        },
      },
    },
  }), []);

  const chartHeight = useMemo(() => Math.max(360, Math.min(rowsToUse.length * 28, 620)), [rowsToUse.length]);

  const handleClose = useCallback(() => {
    onClose();
  }, [onClose]);

  return (
    <$Panel
      title="Trip Length Frequency Distribution"
      onClose={handleClose}
      initialSize={{ width: panWidth, height: panHeight }}
      initialPosition={{ top: window.innerHeight * 0.009, left: window.innerWidth * 0.053 }}
      minContentFontSize={16}
      style={{
        backgroundColor: 'var(--panelColorNormal)',
        display: 'flex',
        flexDirection: 'column',
        overflow: 'hidden',
        margin: '3rem',
      }}
    >
      <div style={{ flex: '0 0 auto', display: 'flex', justifyContent: 'center', gap: '2rem', margin: '2rem' }}>
        <button
          onClick={() => setIsGrouped(prev => !prev)}
          style={{
            padding: '0.5rem 1rem',
            backgroundColor: '#34495e',
            color: 'white',
            border: 'none',
            borderRadius: '4px',
            cursor: 'pointer',
            fontSize: '16px',
          }}
          aria-pressed={isGrouped}
        >
          {isGrouped ? 'Show 1 km Bins' : 'Show Grouped Bins'}
        </button>

        <button
          onClick={() => setShowSummaryStats(prev => !prev)}
          style={{
            padding: '0.5rem 1rem',
            backgroundColor: '#34495e',
            color: 'white',
            border: 'none',
            borderRadius: '4px',
            cursor: 'pointer',
            fontSize: '16px',
          }}
          aria-pressed={showSummaryStats}
        >
          {showSummaryStats ? 'Hide Summary Stats' : 'Show Summary Stats'}
        </button>
      </div>

      <div
        style={{
          flex: '0 0 auto',
          color: 'white',
          display: 'flex',
          justifyContent: 'center',
          gap: '3rem',
          padding: '0 2rem 1rem 2rem',
          fontFamily: commonFont.family,
          fontSize: `${commonFont.size}px`,
        }}
      >
        <span>Total trips: {totalTrips}</span>
        <span>Avg: {averageDistanceFromBins(details)} km</span>
        <span>P50: {percentileFromBins(details, 0.5)} km</span>
        <span>P90: {percentileFromBins(details, 0.9)} km</span>
      </div>

      {showSummaryStats && (
        <div
          style={{
            flex: '0 0 auto',
            padding: '1rem 2rem',
            backgroundColor: 'rgba(0,0,0,0.45)',
            borderRadius: '4px',
            margin: '0 2rem 1rem 2rem',
            maxHeight: '260px',
            overflowY: 'auto',
          }}
        >
          <div
            className="labels_L7Q row_S2v"
            style={{ width: '100%', padding: '0.75rem 0', borderBottom: '1px solid white', color: 'white' }}
          >
            <div style={{ width: '50%', paddingLeft: '1rem' }}><b>Km Bin</b></div>
            <div style={{ width: '50%', textAlign: 'center' }}><b>Trips</b></div>
          </div>

          {rowsToUse.map((row, index) => (
            <TripLengthFrequencyRow key={`${row.label}-${index}`} row={row} index={index} />
          ))}
        </div>
      )}

      <div style={{ flex: '1 1 auto', width: '100%', overflowY: 'auto', padding: '0 1rem 1rem 1rem' }}>
        {totalTrips === 0 ? (
          <p style={{ color: 'white' }}>No active trip length data is available yet.</p>
        ) : (
          <div style={{ height: `${chartHeight}px`, width: '100%' }}>
            <Bar data={chartData} options={chartOptions} />
          </div>
        )}
      </div>
    </$Panel>
  );
};

export default TripLengthFrequency;
