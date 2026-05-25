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

interface TransferDistributionInfo {
  index: number;
  trips: number;
}

interface TransferAverageInfo {
  trips: number;
}

interface TransferDistributionProps {
  onClose: () => void;
}

interface TransferDistributionRow {
  label: string;
  trips: number;
  share: number;
}

const commonFont = {
  family: 'Arial, sans-serif',
  size: 18,
  weight: 'normal' as const,
};

const transferLabels = ['0', '1', '2', '3+'];

const normalizeRows = (details: TransferDistributionInfo[]): TransferDistributionRow[] => {
  const tripsByBin = transferLabels.map((_, index) => {
    const row = details.find(info => info.index === index);
    return row?.trips ?? 0;
  });

  const total = tripsByBin.reduce((sum, trips) => sum + trips, 0);

  return tripsByBin.map((trips, index) => ({
    label: transferLabels[index],
    trips,
    share: total > 0 ? trips / total : 0,
  }));
};

const TransferDistribution: FC<TransferDistributionProps> = ({ onClose }) => {
  const [details, setDetails] = useState<TransferDistributionInfo[]>([]);
  const [averageDetails, setAverageDetails] = useState<TransferAverageInfo[]>([]);
  const [showSummaryStats, setShowSummaryStats] = useState<boolean>(false);

  useDataUpdate('pathTripsInfo.transferDistributionDetails', data => {
    setDetails(Array.isArray(data) ? data : []);
  });

  useDataUpdate('pathTripsInfo.transfersDetails', data => {
    setAverageDetails(Array.isArray(data) ? data : []);
  });

  const panWidth = window.innerWidth * 0.36;
  const panHeight = window.innerHeight * 0.5;

  const rows = useMemo(() => normalizeRows(details), [details]);
  const totalTrips = useMemo(() => rows.reduce((sum, row) => sum + row.trips, 0), [rows]);
  const averageTransfers = useMemo(() => {
    if (typeof averageDetails[0]?.trips === 'number') {
      return (averageDetails[0].trips / 100).toFixed(2);
    }

    if (totalTrips <= 0) return '0.00';

    const weighted = rows.reduce((sum, row, index) => {
      const transferCount = index === rows.length - 1 ? 3 : index;
      return sum + row.trips * transferCount;
    }, 0);

    return (weighted / totalTrips).toFixed(2);
  }, [averageDetails, rows, totalTrips]);

  const chartData = useMemo(() => ({
    labels: rows.map(row => row.label),
    datasets: [
      {
        label: 'Linked transit trips',
        data: rows.map(row => row.trips),
        backgroundColor: ['#4DA6FF', '#33CC33', '#FF8000', '#CC0066'],
      },
    ],
  }), [rows]);

  const chartOptions = useMemo(() => ({
    indexAxis: 'x' as const,
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
      title: {
        display: true,
        text: 'Transfer Count Distribution',
        color: 'white',
        font: commonFont,
      },
      legend: {
        display: false,
      },
      tooltip: {
        callbacks: {
          title: (items: any[]) => `${items?.[0]?.label ?? ''} transfers`,
          label: (item: any) => `${item.parsed.y} linked transit trips`,
        },
      },
    },
    scales: {
      y: {
        title: {
          display: true,
          text: 'Linked transit trips',
          color: 'white',
          font: commonFont,
        },
        ticks: {
          color: 'white',
          font: { ...commonFont, size: commonFont.size - 4 },
          precision: 0,
        },
        grid: {
          color: 'rgba(255,255,255,0.1)',
        },
      },
      x: {
        title: {
          display: true,
          text: 'Transfers per linked transit trip',
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
    },
  }), []);

  const handleClose = useCallback(() => {
    onClose();
  }, [onClose]);

  return (
    <$Panel
      title="Transfer Count Distribution"
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
      <div style={{ flex: '0 0 auto', display: 'flex', justifyContent: 'center', margin: '2rem' }}>
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
        <span>Transit trips: {totalTrips}</span>
        <span>Avg transfers: {averageTransfers}</span>
      </div>

      {showSummaryStats && (
        <div
          style={{
            flex: '0 0 auto',
            padding: '1rem 2rem',
            backgroundColor: 'rgba(0,0,0,0.45)',
            borderRadius: '4px',
            margin: '0 2rem 1rem 2rem',
            maxHeight: '220px',
            overflowY: 'auto',
          }}
        >
          <div
            className="labels_L7Q row_S2v"
            style={{ width: '100%', padding: '0.75rem 0', borderBottom: '1px solid white', color: 'white' }}
          >
            <div style={{ width: '34%', paddingLeft: '1rem' }}><b>Transfers</b></div>
            <div style={{ width: '33%', textAlign: 'center' }}><b>Trips</b></div>
            <div style={{ width: '33%', textAlign: 'center' }}><b>Share</b></div>
          </div>

          {rows.map((row, index) => (
            <div
              key={row.label}
              className="labels_L7Q row_S2v"
              style={{
                width: '100%',
                padding: '0.75rem 0',
                backgroundColor: index % 2 === 0 ? 'rgba(255,255,255,0.08)' : 'transparent',
                color: 'white',
              }}
            >
              <div style={{ width: '34%', paddingLeft: '1rem' }}>{row.label}</div>
              <div style={{ width: '33%', textAlign: 'center' }}>{row.trips}</div>
              <div style={{ width: '33%', textAlign: 'center' }}>{`${(row.share * 100).toFixed(1)}%`}</div>
            </div>
          ))}
        </div>
      )}

      <div style={{ flex: '1 1 auto', width: '100%', overflowY: 'auto', padding: '0 1rem 1rem 1rem' }}>
        {totalTrips === 0 ? (
          <p style={{ color: 'white' }}>No transit transfer data is available yet.</p>
        ) : (
          <div style={{ height: '320px', width: '100%' }}>
            <Bar data={chartData} options={chartOptions} />
          </div>
        )}
      </div>
    </$Panel>
  );
};

export default TransferDistribution;
