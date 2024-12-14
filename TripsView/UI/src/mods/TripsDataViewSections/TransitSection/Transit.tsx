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
  hour: number;
  total: number;
  bus: number;
  tram: number; 
  subway: number; 
  train: number; 
  ship: number;
  airplane: number;
}


// Define aggregated info interface
interface AggregatedInfo {
  label: string;
  total: number;
  bus: number;
  tram: number;
  subway: number;
  train: number;
  ship: number;
  airplane: number;
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
const HOUR_RANGES = [
  { label: '0-5', min: 0, max: 5 },
  { label: '6-9', min: 6, max: 9 },
  { label: '10-14', min: 10, max: 14 },
  { label: '15-19', min: 15, max: 19 },
  { label: '20-24', min: 20, max: 24 },
];

// Optimized aggregation function
const aggregateDataByHourRanges = (details: Info[]): AggregatedInfo[] => {
  const aggregated = HOUR_RANGES.map(range => ({
    label: range.label,
    total: 0,
    bus: 0,
    tram: 0,
    subway: 0,
    train: 0,
    ship: 0,
    airplane: 0,
  }));

  details.forEach(info => {
    const index = HOUR_RANGES.findIndex(
      range =>
        info.hour >= range.min &&
        (info.hour < range.max || (info.hour === range.max && range.max ===24))
    );

    if (index !== -1) {
      const agg = aggregated[index];
      agg.total += info.total;
      agg.bus += info.bus;
      agg.tram += info.tram;
      agg.subway += info.subway;
      agg.train += info.train;
      agg.ship += info.ship;
      agg.airplane += info.airplane;
    }
  });

  return aggregated;
};

// Optimized function to group details by individual hour
const groupDetailsByHour = (details: Info[]): AggregatedInfo[] => {
  const grouped = details.reduce<Record<number, AggregatedInfo>>((acc, info) => {
    const hour = info.hour;
    if (!acc[hour]) {
      acc[hour] = {
        label: `${hour}`,
        total: 0,
        bus: 0,
        tram: 0,
        subway: 0,
        train: 0,
        ship: 0,
        airplane: 0,
      };
    }
    const agg = acc[hour];
    agg.total += info.total;
    agg.bus += info.bus;
    agg.tram += info.tram;
    agg.subway += info.subway;
    agg.train += info.train;
    agg.ship += info.ship;
    agg.airplane += info.airplane;
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

// TransitLevel Component
const TransitLevel: React.FC<{
  levelColor: string;
  levelName: string;
  levelValues: {
    total: number;
    bus: number;
    tram: number;
    subway: number;
    train: number;
    ship: number;
    airplane: number;
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
      {levelValues.bus}
    </div>
    <div className="row_S2v" style={{ width: '12%', justifyContent: 'center' }}>
      {levelValues.tram}
    </div>
    <div className="row_S2v" style={{ width: '12%', justifyContent: 'center' }}>
      {levelValues.subway}
    </div>
    <div className="row_S2v" style={{ width: '12%', justifyContent: 'center' }}>
      {levelValues.train}
    </div>
    <div className="row_S2v" style={{ width: '12%', justifyContent: 'center' }}>
      {levelValues.ship}
    </div>
    <div className="row_S2v" style={{ width: '12%', justifyContent: 'center' }}>
      {levelValues.airplane}
    </div>
  </div>
);

interface TransitProps {
  onClose: () => void;
}

const Transit: FC<TransitProps> = ({ onClose }) => {
  // State hooks for totals and details
  const [details, setDetails] = useState<Info[]>([]);
  // State hooks for grouping and summary statistics visibility
  const [isGrouped, setIsGrouped] = useState<boolean>(false);
  const [showSummaryStats, setShowSummaryStats] = useState<boolean>(false);

  // Fetch details data using useDataUpdate hook
    useDataUpdate('transitPassengersInfo.transitDetails', data => setDetails(data || []));

  // Panel dimensions
  const panWidth = window.innerWidth * 0.4;
  const panHeight = window.innerHeight * 0.6;

  // Define per-bar height and maximum chart height
  const BAR_HEIGHT = 40;
  const MAX_CHART_HEIGHT = 600;

  // Prepare detailed data for Chart.js with grouping
  const detailedChartData = useMemo(() => {
    const groupedData = groupDetailsByHour(details)
    const sortedHours = groupedData.sort((a, b) => parseInt(a.label) - parseInt(b.label));
  
    return {
      labels: sortedHours.map(data => data.label),
      datasets: [
        {
          label: 'Bus',
          data: sortedHours.map(data => data.bus),
          backgroundColor: '#4DA6FF',
        },
        {
          label: 'Tram',
          data: sortedHours.map(data => data.tram),
          backgroundColor: '#CC0066',
        },
        {
          label: 'Subway',
          data: sortedHours.map(data => data.subway),
          backgroundColor: '#33CC33',
        },
        {
          label: 'Train',
          data: sortedHours.map(data => data.train),
          backgroundColor: '#FF8000',
        },
        {
          label: 'Ship',
          data: sortedHours.map(data => data.ship),
          backgroundColor: '#2EB8B8',
        },
        {
            label: 'Airplane',
            data: sortedHours.map(data => data.airplane),
            backgroundColor: '#BF00FF',
        },
      ],
    };
  }, [details]);

  // Prepare grouped data for Chart.js
  const groupedChartData = useMemo(() => {
    const aggregated = aggregateDataByHourRanges(details);

    return {
      labels: aggregated.map(data => data.label),
      datasets: [
        {
          label: 'Bus',
          data: aggregated.map(data => data.bus),
          backgroundColor: '#4DA6FF',
        },
        {
          label: 'Tram',
          data: aggregated.map(data => data.tram),
          backgroundColor: '#CC0066',
        },
        {
          label: 'Subway',
          data: aggregated.map(data => data.subway),
          backgroundColor: '#33CC33',
        },
        {
          label: 'Train',
          data: aggregated.map(data => data.train),
          backgroundColor: '#FF8000',
        },
        {
          label: 'Ship',
          data: aggregated.map(data => data.ship),
          backgroundColor: '#2EB8B8',
        },
        {
          label: 'Airplane',
          data: aggregated.map(data => data.airplane),
          backgroundColor: '#BF00FF',
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
          text: 'Transit Passengers',
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
            text: 'Number of passengers',
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
            text: isGrouped ? 'Hour Groups' : 'Hours',
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

  // Calculate detailed summary statistics per hour or hour group
  const detailedSummaryStats = useMemo(() => {
    return isGrouped ? aggregateDataByHourRanges(details) : groupDetailsByHour(details);
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
      title="Transit Passengers Per Hour"
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
                <div>Hour</div>
              </div>
              <div className="row_S2v" style={{ width: '11%', justifyContent: 'center' }}>
                Total
              </div>
              <div className="row_S2v" style={{ width: '11%', justifyContent: 'center' }}>
                Bus
              </div>
              <div className="row_S2v" style={{ width: '12%', justifyContent: 'center' }}>
                Tram
              </div>
              <div className="row_S2v small_ExK" style={{ width: '9%', justifyContent: 'center' }}>
                Subway
              </div>
              <div className="row_S2v small_ExK" style={{ width: '9%', justifyContent: 'center' }}>
                Train
              </div>
              <div className="row_S2v small_ExK" style={{ width: '9%', justifyContent: 'center' }}>
                Ship
              </div>
              <div className="row_S2v small_ExK" style={{ width: '9%', justifyContent: 'center' }}>
                Airplane
              </div>
            </div>

            {/* Summary Rows */}
            {detailedSummaryStats.map((stat, index) => (
              <TransitLevel
                key={index}
                levelColor={index % 2 === 0 ? 'rgba(255, 255, 255, 0.1)' : 'transparent'}
                levelName={stat.label}
                levelValues={{
                  total: stat.total,
                  bus: stat.bus,
                  tram: stat.tram,
                  subway: stat.subway,
                  train: stat.train,
                  ship: stat.ship,
                  airplane: stat.airplane,
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

export default Transit;
