// Workplaces.tsx
import React, { FC, useCallback, useEffect, useState } from 'react';
import useDataUpdate from 'mods/use-data-update';
import $Panel from 'mods/panel';
import { Tooltip } from "cs2/ui";

const tableTooltips = {
    linkedTrips: "Linked trips are complete trips from the origin to the destination including transfers.",
    unlinkedTrips: "Unlinked trips are the total number of boardings in each transit mode. If a passenger does a transfer to another vehicle, that will be counted as another unlinked trip.",
    transfers: "Average number of transfers per passenger.",
};


// Define interfaces for component props
interface LinkedTripsModeValues {
    trips: number;
    [key: string]: any;
}
interface LinkedTripsModeProps {
    levelColor?: string;
    levelName: string;
    levelValues: LinkedTripsModeValues;
    showAll?: boolean;
}

interface TransfersValues {
    trips: number;
    [key: string]: any;
}
interface TransfersProps {
    levelColor?: string;
    levelName: string;
    levelValues: TransfersValues;
    showAll?: boolean;
}

// LinkedTrips Component
const LinkedTripsMode: React.FC<LinkedTripsModeProps> = ({
    levelColor,
    levelName,
    levelValues,
    showAll = true,
}) => {
    return (
        <div
            className="labels_L7Q row_S2v"
            style={{ width: '99%', paddingTop: '1rem', paddingBottom: '1rem' }}
        >
            <div style={{ width: '1%' }}></div>
            <div style={{ alignItems: 'left', width: '60%' }}>
                <div>{levelName}</div>
            </div>
            <div style={{ width: '40%', justifyContent: 'left' }}>
                {`${levelValues['trips']/10}%`}
            </div>
        </div>
    );
};

const Transfers: React.FC<TransfersProps> = ({
    levelColor,
    levelName,
    levelValues,
    showAll = true,
}) => {
    return (
        <div
            className="labels_L7Q row_S2v"
            style={{ width: '99%', paddingTop: '1rem', paddingBottom: '1rem' }}
        >
            <div style={{ width: '1%' }}></div>
            <div style={{ alignItems: 'left', width: '60%' }}>
                <div><b>{levelName}</b></div>
            </div>
            <div style={{ width: '40%', justifyContent: 'left' }}>
                {`${levelValues['trips']/100}`}
            </div>
        </div>
    );
};

// Main LinkedTrips Component
interface LinkedTripsProps {
    onClose: () => void;
}

// Simple horizontal line
const DataDivider: React.FC = () => {
    return (
        <div style={{ display: 'flex', height: '4rem', flexDirection: 'column', justifyContent: 'center' }}>
            <div style={{ borderBottom: '1px solid gray' }}></div>
        </div>
    );
};

const LinkedTrips: FC<LinkedTripsProps> = ({ onClose }) => {
    // State for controlling the visibility of the panel
    const [isPanelVisible, setIsPanelVisible] = useState(true);

    // Data fetching and other logic
    const [linkedTrips, setLinkedTrips] = useState<LinkedTripsModeValues[]>([]);
    useDataUpdate('pathTripsInfo.pathTripsDetails', setLinkedTrips);

    const [transfers, setTransfers] = useState<TransfersValues[]>([]);
    useDataUpdate('pathTripsInfo.transfersDetails', setTransfers);

    const [unlinkedTrips, setUnlinkedTrips] = useState<LinkedTripsModeValues[]>([]);
    useDataUpdate('transit.transitUnlinkedDetails', setUnlinkedTrips);

    const defaultPosition = { top: window.innerHeight * 0.05, left: window.innerWidth * 0.005 } ;
    const [panelPosition, setPanelPosition] = useState(defaultPosition);
    const handleSavePosition = useCallback((position: { top: number; left: number }) => {
        setPanelPosition(position);
    }, []);
    const [lastClosedPosition, setLastClosedPosition] = useState(defaultPosition);

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
            initialSize={{ width: window.innerWidth * 0.18, height: window.innerHeight * 0.355 }}
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
                        {/* Linked Trips Table Header with Tooltip */}
                        <Tooltip tooltip={tableTooltips.linkedTrips}>
                            <div
                                className="labels_L7Q row_S2v"
                                style={{ width: '99%', paddingTop: '1rem', paddingBottom: '1rem' }}
                            >
                                <div style={{ width: '1%' }}></div>
                                <div style={{ alignItems: 'left', width: '60%' }}>
                                    <div><b>{"Mode"}</b></div>
                                </div>
                                <div style={{ width: '40%', justifyContent: 'left' }}>
                                    <b>{"Linked Trips"}</b>
                                </div>
                            </div>
                        </Tooltip>
                    <DataDivider />
                    <div style={{ height: '5rem' }}></div>
                    <LinkedTripsMode
                        levelName="Vehicle"
                        levelValues={linkedTrips[0]}
                    />
                    <LinkedTripsMode
                        levelName="Transit"
                        levelValues={linkedTrips[1]}
                    />
                    <LinkedTripsMode
                        levelName="Walk"
                        levelValues={linkedTrips[2]}
                    />
                    <DataDivider />
                </div>
            )}
            {unlinkedTrips.length === 0 ? (
                <p>Waiting...</p>
            ) : (
                <div>
                    {/* Your existing content rendering */}
                    {/* Adjusted heights as needed */}
                    <div style={{ height: '10rem' }}></div>
                        {/* Unlinked Trips Table Header with Tooltip */}
                        <Tooltip tooltip={tableTooltips.unlinkedTrips}>
                            <div
                                className="labels_L7Q row_S2v"
                                style={{ width: '99%', paddingTop: '1rem', paddingBottom: '1rem' }}
                            >
                                <div style={{ width: '1%' }}></div>
                                <div style={{ alignItems: 'left', width: '60%' }}>
                                    <div><b>{"Transit Mode"}</b></div>
                                </div>
                                <div style={{ width: '40%', justifyContent: 'left' }}>
                                    <b>{"Unlinked Trips"}</b>
                                </div>
                            </div>
                        </Tooltip>
                    <DataDivider />
                    <div style={{ height: '5rem' }}></div>
                    <LinkedTripsMode
                        levelName="Bus"
                        levelValues={unlinkedTrips[0]}
                    />
                    <LinkedTripsMode
                        levelName="Tram"
                        levelValues={unlinkedTrips[1]}
                    />
                    <LinkedTripsMode
                        levelName="Subway"
                        levelValues={unlinkedTrips[2]}
                        />
                    <LinkedTripsMode
                        levelName="Train"
                        levelValues={unlinkedTrips[3]}
                        />
                    <LinkedTripsMode
                        levelName="Ship"
                        levelValues={unlinkedTrips[4]}
                        />
                    <LinkedTripsMode
                        levelName="Airplane"
                        levelValues={unlinkedTrips[5]}
                    />
                    <DataDivider />
                    <div style={{ height: '5rem' }}></div>
                    <Transfers
                        levelName="Avg. Number of Transfers per Passenger:"
                        levelValues={transfers[0]}
                    />
                    <DataDivider />
                </div>
            )}
        </$Panel>
    );
};

export default LinkedTrips;

