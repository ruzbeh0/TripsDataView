import React, { useState, useMemo, KeyboardEvent, useCallback, FC } from 'react';
import useDataUpdate from 'mods/use-data-update';
import $Panel from 'mods/panel';
import mod from "mod.json";

// Import Chart.js components
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
import { bindValue, useValue } from 'cs2/api';

// Register Chart.js components
ChartJS.register(CategoryScale, LinearScale, BarElement, Tooltip, Legend, Title);
// Define interfaces for component props
interface AlignedParagraphProps {
  left: string;
  right: number;
}

interface Info {
  mode: number;
  pkt: number;
}

const modeLabels: { [key: number]: string } = {
    0: "Bus",
    3: "Tram",
    8: "Subway",
    1: "Train",
    4: "Ship",
    7: "Airplane",
};

// Define aggregated info interface
interface AggregatedInfo {
  label: string;
  pkt: number;
}

// Define a common font configuration
const commonFont = {
  family: 'Arial, sans-serif',
  size: 14,
  weight: 'normal' as const,
};
const yaxisfont = {
  family: 'Arial, sans-serif',
  size: 11,
  weight: 'normal' as const,
};

// Optimized function to group details by individual hour
const groupDetailsByMode = (details: Info[]): AggregatedInfo[] => {
    const grouped = details.reduce<Record<number, AggregatedInfo>>((acc, info) => {
        const mode = info.mode;
        if (!acc[mode]) {
            acc[mode] = {
                label: modeLabels[mode] || `Unknown (${mode})`,
                pkt: 0,
            };
        }
        acc[mode].pkt += info.pkt;
        return acc;
    }, {});

    return Object.values(grouped);
};


// AlignedParagraph Component for Summary
const AlignedParagraph: React.FC<AlignedParagraphProps> = ({ left, right }) => (
  <div
    className="labels_L7Q row_S2v"
    style={{
      width: '100%',
      padding: '0.5rem 0',
      display: 'flex',
      justifyContent: 'space-between',
      color: 'white',
      fontSize: `${commonFont.size}px`,
      fontFamily: commonFont.family,
      fontWeight: commonFont.weight,
    }}
  >
    <div style={{ textAlign: 'left' }}>{left}</div>
    <div style={{ textAlign: 'right' }}>{right}</div>
  </div>
);

// PKTLevel Component
const PKTLevel: React.FC<{
  levelColor: string;
  levelName: string;
  levelValues: {
    pkt: number;
  };
  pkt: number;
}> = ({ levelColor, levelName, levelValues, pkt }) => (
  <div
    className="labels_L7Q row_S2v"
    style={{ width: '99%', padding: '1rem 0', backgroundColor: levelColor }}
  >
    <div style={{ width: '1%' }}></div>
    <div style={{ display: 'flex', alignItems: 'center', width: '22%' }}>
      <div
        className="symbol_aAH"
        style={{
          backgroundColor: levelColor,
          width: '1.2em',
          height: '1.2em',
          marginRight: '0.5rem',
          borderRadius: '50%',
        }}
      ></div>
      <div>{levelName}</div>
    </div>
    <div className="row_S2v" style={{ width: '84%', justifyContent: 'center' }}>
      {pkt}
    </div>
  </div>
);

interface PKTProps {
  onClose: () => void;
}

const PKT: FC<PKTProps> = ({ onClose }) => {
  // State hooks for totals and details
  const [details, setDetails] = useState<Info[]>([]);
  // State hooks for grouping and summary statistics visibility
  const [isGrouped, setIsGrouped] = useState<boolean>(false);
  const [showSummaryStats, setShowSummaryStats] = useState<boolean>(false);

  // Fetch details data using useDataUpdate hook
    useDataUpdate('pathTripsInfo.pktDetails', data => setDetails(data || []));

  // Panel dimensions
  const panWidth = window.innerWidth * 0.4;
  const panHeight = window.innerHeight * 0.6;

  // Define per-bar height and maximum chart height
  const BAR_HEIGHT = 40;
  const MAX_CHART_HEIGHT = 600;

  // Prepare detailed data for Chart.js with grouping
    const detailedChartData = useMemo(() => {
        const groupedData = groupDetailsByMode(details);
        const sortedData = groupedData.sort((a, b) =>
            Object.keys(modeLabels).indexOf(a.label) - Object.keys(modeLabels).indexOf(b.label)
        );

        return {
            labels: sortedData.map(data => data.label), // Use labels instead of numbers
            datasets: [
                {
                    label: '', // Removing the label to prevent it from showing in the legend
                    data: sortedData.map(data => data.pkt),
                    backgroundColor: ['#4DA6FF', '#CC0066', '#33CC33', '#FF8000', '#2EB8B8', '#BF00FF'],
                },
            ],
        };
    }, [details]);


  const baseFontSize = 8; // Base font size
  const fontSizeMultiplier = Math.max(0.5, 10) 
  const dynamicFontSize = Math.max(8, Math.floor(baseFontSize * fontSizeMultiplier)); // Never go below 8px
  // Chart options with aligned font settings
  const chartOptions = useMemo(
    () => ({
      
      indexAxis: 'x' as const,
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        title: {
          display: false,
        },
        legend: {
          display: false, // Hide the legend
        },
      },
      scales: {
        y: {
          stacked: true,
          title: {
            display: true,
            text: 'Passenger Kilometers',
            color: 'white',
            font: commonFont,
          },
          ticks: {
            color: 'white',
            font: { ...commonFont, size: commonFont.size - 4 },

          },
          grid: {
            color: 'rgba(255, 255, 255, 0.1)',
          },
        },
        x: {
          stacked: true,
          title: {
            display: true,
            text: 'Transit Mode',
            color: 'white',
            font: commonFont,
          },
          ticks: {
              color: 'white',
              font: { ...commonFont, size: commonFont.size - 4 },

          },
          grid: {
            color: 'rgba(255, 255, 255, 0.1)',
          },
        },
      },
    }),
    [isGrouped]
  );

  // Choose chart data based on isGrouped
  const chartDataToUse = detailedChartData;

  // Calculate dynamic chart height with a new maximum limit
  const chartHeight = useMemo(() => {
    const baseHeight = 10; // Base height per data point
    const heightMultiplier = Math.max(1, 10); // Same multiplier logic as above
    const dataLength = details.length;
    return Math.min(dataLength * baseHeight * heightMultiplier, MAX_CHART_HEIGHT);
  }, [isGrouped, details.length]);

  // Calculate detailed summary statistics per hour or hour group
  const detailedSummaryStats = useMemo(() => {
    return groupDetailsByMode(details);
  }, [details, isGrouped]);

  // Define functions to handle keypress on buttons for accessibility
  const handleToggleKeyPress = (
    e: KeyboardEvent<HTMLButtonElement>,
    toggleFunction: () => void
  ) => {
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      toggleFunction();
    }
  };

  // NEW: Function to handle data reset
  const handleResetData = () => {
    setDetails([]);
  };

  // New state to control panel visibility
  const [isPanelVisible, setIsPanelVisible] = useState(true);

  // Handler for closing the panel
  const handleClose = useCallback(() => {
    onClose();
  }, [onClose]);

  if (!isPanelVisible) {
    return null;
  }

  return (
    <$Panel
      title="Passenger Kilometers Travelled"
      onClose={handleClose}
      initialSize={{ width: panWidth, height: panHeight }}
      initialPosition={{ top: window.innerHeight * 0.009, left: window.innerWidth * 0.053 }}
      style={{
        backgroundColor: 'var(--panelColorNormal)',
        display: 'flex',
        flexDirection: 'column',
        overflow: 'hidden',
        margin: '3rem',
      }}
    >

      {/* Spacer */}
      <div style={{ flex: '0 0 auto', height: '1rem' }}></div>

      {/* Toggle Buttons */}
<div
  style={{
    flex: '0 0 auto',
    display: 'flex',
    justifyContent: 'center',
    margin: '5rem', // Increased gap between buttons
  }}
>

  <button
    onClick={() => setShowSummaryStats(prev => !prev)}
    onKeyPress={e => handleToggleKeyPress(e, () => setShowSummaryStats(prev => !prev))}
    style={{
      padding: '0.5rem 1rem',
      backgroundColor: '#34495e',
      color: 'white',
      border: 'none',
      borderRadius: '4px',
      cursor: 'pointer',
      fontSize: '14px',
      margin: '3rem',
    }}
    aria-pressed={showSummaryStats}
    aria-label={showSummaryStats ? 'Hide Summary Statistics' : 'Show Summary Statistics'}
  >
    {showSummaryStats ? 'Hide Summary Stats' : 'Show Summary Stats'}
  </button>

</div>

      {/* Spacer */}
      <div style={{ flex: '0 0 auto', height: '1rem' }}></div>

      {/* Conditionally Render Summary Statistics */}
      {showSummaryStats && (
        <div
          style={{
            flex: '0 0 auto',
            padding: '2rem',
            backgroundColor: 'rgba(0, 0, 0, 0.5)',
            borderRadius: '4px',
            margin: '0 2rem',
            overflow: 'hidden',
            maxHeight: '300px',
          }}
        >
          <h3 style={{ color: 'white', marginBottom: '0.5rem' }}>Summary Statistics</h3>

          {/* Scrollable Container */}
          <div
            style={{
              overflowY: 'auto',
              maxHeight: '250px',
              paddingRight: '10px',
            }}
          >
            {/* Header Row */}
            <div
              className="labels_L7Q row_S2v"
              style={{ width: '100%', padding: '1rem 0', borderBottom: '1px solid white' }}
            >
              <div style={{ width: '1%' }}></div>
              <div style={{ display: 'flex', alignItems: 'center', width: '22%' }}>
                <div>Mode</div>
              </div>
              <div className="row_S2v" style={{ width: '70%', justifyContent: 'center' }}>
                PKT
              </div>
            </div>

            {/* Summary Rows */}
            {detailedSummaryStats.map((stat, index) => (
              <PKTLevel
                key={index}
                levelColor={index % 2 === 0 ? 'rgba(255, 255, 255, 0.1)' : 'transparent'}
                levelName={stat.label}
                levelValues={{
                  pkt: stat.pkt,
                }}
                pkt={stat.pkt}
              />
            ))}
          </div>
        </div>
      )}

      {/* Spacer */}
      <div style={{ flex: '0 0 auto', height: '1rem' }}></div>

      {/* Scrollable Chart Container */}
      <div style={{ flex: '1 1 auto', width: '100%', overflowY: 'auto' }}>
        {details.length === 0 ? (
          <p style={{ color: 'white' }}>No data available to display the chart.</p>
        ) : (
          <div style={{ height: `${chartHeight}px`, width: '100%' }}>
            <Bar data={chartDataToUse} options={chartOptions} />
          </div>
        )}
      </div>
    </$Panel>
  );
};

// Helper function to get distinct colors for datasets
const getColor = (index: number) => {
  const colors = [
    '#624532',
    '#4DA6FF',
    '#CC0066',
    '#33CC33',
    '#FF8000',
    '#2EB8B8',
    '#BF00FF',
    '#FF5733',
    '#C70039',
    '#900C3F',
    '#581845',
  ];
  return colors[index % colors.length];
};

export default PKT;
