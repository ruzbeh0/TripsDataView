// Workplaces.tsx
import React, { FC, useCallback, useEffect, useState } from 'react';
import useDataUpdate from 'mods/use-data-update';
import $Panel from 'mods/panel';


// Define interfaces for component props
interface LinkedTripsModeValues {
    trips: number | string;
    [key: string]: any;
}
interface LinkedTripsModeProps {
    levelColor?: string;
    levelName: string;
    levelValues: LinkedTripsModeValues;
    total: number;
    showAll?: boolean;
}

// LinkedTrips Component
const LinkedTripsMode: React.FC<LinkedTripsModeProps> = ({
    levelColor,
    levelName,
    levelValues,
    total,
    showAll = true,
}) => {
    const percent =
        total > 0 && typeof levelValues.total === 'number'
            ? `${((100 * levelValues.total) / total).toFixed(1)}%`
            : '';

    return (
        <div
            className="labels_L7Q row_S2v"
            style={{ width: '99%', paddingTop: '1rem', paddingBottom: '1rem' }}
        >
            <div style={{ width: '1%' }}></div>
            <div style={{ display: 'flex', alignItems: 'center', width: '20%' }}>
                {levelColor && (
                    <div
                        className="symbol_aAH"
                        style={{ backgroundColor: levelColor, width: '1.2em' }}
                        aria-hidden="true"
                    ></div>
                )}
                <div>{levelName}</div>
            </div>
            <div
                className="row_S2v"
                style={{ width: '8%', justifyContent: 'center' }}
            >
                {levelValues['total']}
            </div>
            <div
                className="row_S2v"
                style={{ width: '7%', justifyContent: 'center' }}
            >
                {percent}
            </div>
            <div
                className="row_S2v small_ExK"
                style={{ width: '6%', justifyContent: 'center' }}
            >
                {levelValues['trips']}
            </div>
        </div>
    );
};

// Main LinkedTrips Component
interface LinkedTripsProps {
    onClose: () => void;
}

const LinkedTrips: FC<LinkedTripsProps> = ({ onClose }) => {
    // State for controlling the visibility of the panel
    const [isPanelVisible, setIsPanelVisible] = useState(true);

    // Data fetching and other logic
    const [linkedTrips, setLinkedTrips] = useState<LinkedTripsModeValues[]>([]);
    useDataUpdate('pathTripsInfo.pathTripsDetails', setLinkedTrips);

    const defaultPosition = { top: window.innerHeight * 0.05, left: window.innerWidth * 0.005 } ;
    const [panelPosition, setPanelPosition] = useState(defaultPosition);
    const handleSavePosition = useCallback((position: { top: number; left: number }) => {
        setPanelPosition(position);
    }, []);
    const [lastClosedPosition, setLastClosedPosition] = useState(defaultPosition);
    const headers: LinkedTripsModeValues = {
        total: 'Total',
        trips: 'Linked Trips',
    };

    // Handler for closing the panel
    const handleClose = useCallback(() => {
        setLastClosedPosition(panelPosition); // Save the current position before closing
        setIsPanelVisible(false);
        onClose();
    }, [onClose, panelPosition]);

    useEffect(() => {
        if (!isPanelVisible) {
            setPanelPosition(lastClosedPosition);
        }
    }, [isPanelVisible, lastClosedPosition]);

    if (!isPanelVisible) {
        return null;
    }

    return (
        <$Panel
            title="Mode Shares"
            onClose={handleClose}
            initialSize={{ width: window.innerWidth * 0.45, height: window.innerHeight * 0.255 }}
            initialPosition={panelPosition}
            onSavePosition={handleSavePosition}
        >
            {linkedTrips.length === 0 ? (
                <p>Waiting...</p>
            ) : (
                <div>
                    {/* Your existing content rendering */}
                    {/* Adjusted heights as needed */}
                    <div style={{ height: '10rem' }}></div>
                    <LinkedTripsMode
                        levelName="Mode"
                        levelValues={headers}
                        total={0}
                    />
                    <div style={{ height: '5rem' }}></div>
                    <LinkedTripsMode
                        levelColor="#808080"
                        levelName="Vehicle"
                        levelValues={linkedTrips[0]}
                        total={Number(linkedTrips[0]) + Number(linkedTrips[1]) + Number(linkedTrips[2])}
                    />
                    <LinkedTripsMode
                        levelColor="#B09868"
                        levelName="Transit"
                        levelValues={linkedTrips[1]}
                        total={Number(linkedTrips[0]) + Number(linkedTrips[1]) + Number(linkedTrips[2])}
                    />
                    <LinkedTripsMode
                        levelColor="#368A2E"
                        levelName="Pedestrian"
                        levelValues={linkedTrips[2]}
                        total={Number(linkedTrips[0]) + Number(linkedTrips[1]) + Number(linkedTrips[2])}
                    />
                </div>
            )}
        </$Panel>
    );
};

export default LinkedTrips;

// Registering the panel with HookUI (if needed)
// window._$hookui.registerPanel({
//     id: 'infoloom.workplaces',
//     name: 'InfoLoom: Workplaces',
//     icon: 'Media/Game/Icons/Workers.svg',
//     component: $Workplaces,
// });
