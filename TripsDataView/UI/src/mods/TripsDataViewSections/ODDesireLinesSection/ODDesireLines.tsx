import React, { FC, useCallback, useEffect, useState } from 'react';
import engine from 'cohtml/cohtml';
import useDataUpdate from 'mods/use-data-update';
import $Panel from 'mods/panel';

interface ODDesireLinesProps {
  onClose: () => void;
}

interface ODDesireLinesSummary {
  enabled: boolean;
  districts: number;
  flows: number;
  trips: number;
  maxFlow: number;
}

const emptySummary: ODDesireLinesSummary = {
  enabled: false,
  districts: 0,
  flows: 0,
  trips: 0,
  maxFlow: 0,
};

const ODDesireLines: FC<ODDesireLinesProps> = ({ onClose }) => {
  const [summary, setSummary] = useState<ODDesireLinesSummary>(emptySummary);
  const [enabled, setEnabled] = useState<boolean>(true);

  useDataUpdate('odDesireLinesInfo.summary', data => {
    setSummary(data || emptySummary);
  });

  useEffect(() => {
    engine.trigger('odDesireLinesInfo.setOverlayEnabled', enabled);

    return () => {
      engine.trigger('odDesireLinesInfo.setOverlayEnabled', false);
    };
  }, [enabled]);

  const handleClose = useCallback(() => {
    engine.trigger('odDesireLinesInfo.setOverlayEnabled', false);
    onClose();
  }, [onClose]);

  return (
    <$Panel
      title="OD Desire Lines"
      onClose={handleClose}
      initialSize={{ width: window.innerWidth * 0.24, height: window.innerHeight * 0.28 }}
      initialPosition={{ top: window.innerHeight * 0.009, left: window.innerWidth * 0.053 }}
      style={{
        backgroundColor: 'var(--panelColorNormal)',
        display: 'flex',
        flexDirection: 'column',
        overflow: 'hidden',
        margin: '3rem',
      }}
    >
      <div
        style={{
          flex: '0 0 auto',
          display: 'flex',
          justifyContent: 'center',
          padding: '2rem 2rem 1rem 2rem',
        }}
      >
        <button
          onClick={() => setEnabled(prev => !prev)}
          style={{
            padding: '0.5rem 1rem',
            backgroundColor: enabled ? '#2f6f8f' : '#34495e',
            color: 'white',
            border: 'none',
            borderRadius: '4px',
            cursor: 'pointer',
            fontSize: '14px',
          }}
          aria-pressed={enabled}
        >
          {enabled ? 'Hide Overlay' : 'Show Overlay'}
        </button>
      </div>

      <div
        style={{
          color: 'white',
          padding: '1rem 2rem 2rem 2rem',
          fontFamily: 'Arial, sans-serif',
          fontSize: '14px',
          display: 'flex',
          flexDirection: 'column',
          gap: '0.75rem',
        }}
      >
        <div className="labels_L7Q row_S2v" style={{ width: '100%' }}>
          <div style={{ width: '55%' }}>Districts</div>
          <div style={{ width: '45%', textAlign: 'right' }}>{summary.districts}</div>
        </div>
        <div className="labels_L7Q row_S2v" style={{ width: '100%' }}>
          <div style={{ width: '55%' }}>Desire lines</div>
          <div style={{ width: '45%', textAlign: 'right' }}>{summary.flows}</div>
        </div>
        <div className="labels_L7Q row_S2v" style={{ width: '100%' }}>
          <div style={{ width: '55%' }}>Trips</div>
          <div style={{ width: '45%', textAlign: 'right' }}>{summary.trips}</div>
        </div>
        <div className="labels_L7Q row_S2v" style={{ width: '100%' }}>
          <div style={{ width: '55%' }}>Largest line</div>
          <div style={{ width: '45%', textAlign: 'right' }}>{summary.maxFlow}</div>
        </div>
      </div>
    </$Panel>
  );
};

export default ODDesireLines;
