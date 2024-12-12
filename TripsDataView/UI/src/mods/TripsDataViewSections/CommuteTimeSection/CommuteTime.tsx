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
  timeBin: number;
  total: number;
  level1: number;
  level2: number; 
  level3: number; 
  level4: number; 
  level5: number;
}


// Define aggregated info interface
interface AggregatedInfo {
  label: string;
  total: number;
  level1: number;
  level2: number;
  level3: number;
  level4: number;
  level5: number;
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
// Define age ranges as a constant
const TIMEBIN_RANGES = [
  { label: '0-30', min: 0, max: 30 },
  { label: '31-60', min: 31, max: 60 },
  { label: '61-90', min: 61, max: 90 },
  { label: '91-120', min: 91, max: 120 },
  { label: '121-150', min: 121, max: 150 },
  { label: '151-180', min: 151, max: 180 },
  { label: '181-210', min: 181, max: 210 },
  { label: '211+', min: 211, max: 400 },
];

// Optimized aggregation function
const aggregateDataBytimeBinRanges = (details: Info[]): AggregatedInfo[] => {
  const aggregated = TIMEBIN_RANGES.map(range => ({
    label: range.label,
    total: 0,
    level1: 0,
    level2: 0,
    level3: 0,
    level4: 0,
    level5: 0,
  }));

  details.forEach(info => {
    const index = TIMEBIN_RANGES.findIndex(
      range =>
        info.timeBin >= range.min &&
        (info.timeBin < range.max || (info.timeBin === range.max && range.max ===400))
    );

    if (index !== -1) {
      const agg = aggregated[index];
      agg.total += info.total;
      agg.level1 += info.level1;
      agg.level2 += info.level2;
      agg.level3 += info.level3;
      agg.level4 += info.level4;
      agg.level5 += info.level5;
    }
  });

  return aggregated;
};

// Optimized function to group details by individual timeBin
const groupDetailsBytimeBin = (details: Info[]): AggregatedInfo[] => {
  const grouped = details.reduce<Record<number, AggregatedInfo>>((acc, info) => {
    const timeBin = info.timeBin;
    if (!acc[timeBin]) {
      acc[timeBin] = {
        label: `${timeBin}`,
        total: 0,
        level1: 0,
        level2: 0,
        level3: 0,
        level4: 0,
        level5: 0,
      };
    }
    const agg = acc[timeBin];
    agg.total += info.total;
    agg.level1 += info.level1;
    agg.level2 += info.level2;
    agg.level3 += info.level3;
    agg.level4 += info.level4;
    agg.level5 += info.level5;
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

// CommuteTimeLevel Component
const CommuteTimeLevel: React.FC<{
  levelColor: string;
  levelName: string;
  levelValues: {
    total: number;
    level1: number;
    level2: number;
    level3: number;
    level4: number;
    level5: number;
  };
  total: number;
}> = ({ levelColor, levelName, levelValues, total }) => (
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
    <div className="row_S2v" style={{ width: '11%', justifyContent: 'center' }}>
      {total}
    </div>
    <div className="row_S2v" style={{ width: '12%', justifyContent: 'center' }}>
      {levelValues.level1}
    </div>
    <div className="row_S2v" style={{ width: '12%', justifyContent: 'center' }}>
      {levelValues.level2}
    </div>
    <div className="row_S2v" style={{ width: '12%', justifyContent: 'center' }}>
      {levelValues.level3}
    </div>
    <div className="row_S2v" style={{ width: '12%', justifyContent: 'center' }}>
      {levelValues.level4}
    </div>
    <div className="row_S2v" style={{ width: '12%', justifyContent: 'center' }}>
      {levelValues.level5}
    </div>
  </div>
);

interface CommuteTimeProps {
  onClose: () => void;
}

const CommuteTime: FC<CommuteTimeProps> = ({ onClose }) => {
  // State hooks for totals and details
  const [details, setDetails] = useState<Info[]>([]);
  // State hooks for grouping and summary statistics visibility
  const [isGrouped, setIsGrouped] = useState<boolean>(false);
  const [showSummaryStats, setShowSummaryStats] = useState<boolean>(false);

  // Fetch details data using useDataUpdate hook
    useDataUpdate('commuteTimeInfo.commuteTimeDetails', data => setDetails(data || []));

  // Panel dimensions
  const panWidth = window.innerWidth * 0.4;
  const panHeight = window.innerHeight * 0.6;

  // Define per-bar height and maximum chart height
  const BAR_HEIGHT = 40;
  const MAX_CHART_HEIGHT = 600;

  // Prepare detailed data for Chart.js with grouping
  const detailedChartData = useMemo(() => {
    const groupedData = groupDetailsBytimeBin(details)
    const sortedtimeBins = groupedData.sort((a, b) => parseInt(a.label) - parseInt(b.label));
  
    return {
      labels: sortedtimeBins.map(data => data.label),
      datasets: [
        {
          label: 'Uneducated',
          data: sortedtimeBins.map(data => data.level1),
          backgroundColor: '#808080',
        },
        {
          label: 'Poorly Educated',
          data: sortedtimeBins.map(data => data.level2),
          backgroundColor: '#B09868',
        },
        {
          label: 'Educated',
          data: sortedtimeBins.map(data => data.level3),
          backgroundColor: '#368A2E',
        },
        {
          label: 'Well Educated',
          data: sortedtimeBins.map(data => data.level4),
          backgroundColor: '#B981C0',
        },
        {
          label: 'Highly Educated',
          data: sortedtimeBins.map(data => data.level5),
          backgroundColor: '#5796D1',
        },
      ],
    };
  }, [details]);

  // Prepare grouped data for Chart.js
  const groupedChartData = useMemo(() => {
    const aggregated = aggregateDataBytimeBinRanges(details);

    return {
      labels: aggregated.map(data => data.label),
      datasets: [
        {
          label: 'Uneducated',
          data: aggregated.map(data => data.level1),
          backgroundColor: '#808080',
        },
        {
          label: 'Poorly Educated',
          data: aggregated.map(data => data.level2),
          backgroundColor: '#B09868',
        },
        {
          label: 'Educated',
          data: aggregated.map(data => data.level3),
          backgroundColor: '#368A2E',
        },
        {
          label: 'Well Educated',
          data: aggregated.map(data => data.level4),
          backgroundColor: '#B981C0',
        },
        {
          label: 'Highly Educated',
          data: aggregated.map(data => data.level5),
          backgroundColor: '#5796D1',
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
          display: true,
          text: 'Commute Time (minutes)',
          color: 'white',
          font: commonFont,
        },
        legend: {
          labels: {
            color: 'white',
            font: commonFont,
          },
        },
      },
      scales: {
        y: {
          stacked: true,
          title: {
            display: true,
            text: 'Frequency',
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
              text: isGrouped ? 'Commute Time (minutes)' : 'Commute Time (minutes)',
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
  const chartDataToUse = isGrouped ? groupedChartData : detailedChartData;

  // Calculate dynamic chart height with a new maximum limit
  const chartHeight = useMemo(() => {
    const baseHeight = 10; // Base height per data point
    const heightMultiplier = Math.max(1, 10); // Same multiplier logic as above
    const dataLength = details.length;
    return Math.min(dataLength * baseHeight * heightMultiplier, MAX_CHART_HEIGHT);
  }, [isGrouped, details.length]);

  // Calculate detailed summary statistics per timeBin or timeBin group
  const detailedSummaryStats = useMemo(() => {
    return isGrouped ? aggregateDataBytimeBinRanges(details) : groupDetailsBytimeBin(details);
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
      title="Commute Time Frequency Distribution"
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
    onClick={() => setIsGrouped(prev => !prev)}
    onKeyPress={e => handleToggleKeyPress(e, () => setIsGrouped(prev => !prev))}
    style={{
      padding: '0.5rem 1rem', // Reduced padding for better appearance
      backgroundColor: '#34495e',
      color: 'white',
      border: 'none',
      borderRadius: '4px',
      cursor: 'pointer',
      fontSize: '14px',
      margin: '3rem',
    }}
    aria-pressed={isGrouped}
    aria-label={isGrouped ? 'Show Detailed View' : 'Show Grouped View'}
  >
    {isGrouped ? 'Show Detailed View' : 'Show Grouped View'}
  </button>

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
                <div>timeBin</div>
              </div>
              <div className="row_S2v" style={{ width: '11%', justifyContent: 'center' }}>
                Total
              </div>
              <div className="row_S2v" style={{ width: '11%', justifyContent: 'center' }}>
                Level1
              </div>
              <div className="row_S2v" style={{ width: '12%', justifyContent: 'center' }}>
                Level2
              </div>
              <div className="row_S2v small_ExK" style={{ width: '9%', justifyContent: 'center' }}>
                Level3
              </div>
              <div className="row_S2v small_ExK" style={{ width: '9%', justifyContent: 'center' }}>
                Level4
              </div>
              <div className="row_S2v small_ExK" style={{ width: '9%', justifyContent: 'center' }}>
                Level5
              </div>
            </div>

            {/* Summary Rows */}
            {detailedSummaryStats.map((stat, index) => (
              <CommuteTimeLevel
                key={index}
                levelColor={index % 2 === 0 ? 'rgba(255, 255, 255, 0.1)' : 'transparent'}
                levelName={stat.label}
                levelValues={{
                  total: stat.total,
                  level1: stat.level1,
                  level2: stat.level2,
                  level3: stat.level3,
                  level4: stat.level4,
                  level5: stat.level5,
                }}
                total={stat.total}
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
    '#808080',
    '#B09868',
    '#368A2E',
    '#B981C0',
    '#5796D1',
    '#BF00FF',
    '#FF5733',
    '#C70039',
    '#900C3F',
    '#581845',
  ];
  return colors[index % colors.length];
};

export default CommuteTime;