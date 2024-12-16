import React, { useCallback, useState, FC } from 'react';
import { Button, FloatingButton, Tooltip } from "cs2/ui";
import icon from "images/magnifiying-glass.svg";
import styles from "./TripsDataViewMenu.module.scss";
import Transit from "mods/TripsDataViewSections/TransitSection/Transit";
import TransitWaiting from "mods/TripsDataViewSections/TransitSection/TransitWaiting";
import TripPurpose from "mods/TripsDataViewSections/TripPurposeSection/TripPurpose";
import CommuteTime from "mods/TripsDataViewSections/CommuteTimeSection/CommuteTime";
import ModeShares from "mods/TripsDataViewSections/ModeSharesSection/ModeShares";


// Define the Section type
type Section = 'Mode Shares' | 'Transit Hourly Passengers' | 'Transit Waiting Time' | 'Trip Purpose' | 'Commute Time';

// Define a new type for components that accept an onClose prop
type SectionComponentProps = {
  onClose: () => void;
};

// Update the sections array type
const sections: { name: Section; displayName: string; component: FC<SectionComponentProps> }[] = [
  { name: 'Mode Shares', displayName: 'ModeShares', component: ModeShares },
  { name: 'Transit Hourly Passengers', displayName: 'Transit', component: Transit },
  { name: 'Transit Waiting Time', displayName: 'TransitWaiting', component: TransitWaiting },
  { name: 'Trip Purpose', displayName: 'TripPurpose', component: TripPurpose },
  { name: 'Commute Time', displayName: 'CommuteTime', component: CommuteTime },
];

const TripsDataViewButton: FC = () => {
  const [mainMenuOpen, setMainMenuOpen] = useState<boolean>(false);
  const [openSections, setOpenSections] = useState<Record<Section, boolean>>({
    'Mode Shares': false,
    'Transit Hourly Passengers': false,
    'Transit Waiting Time': false,
    'Trip Purpose': false,
    'Commute Time': false,
});

  const toggleMainMenu = useCallback(() => {
    setMainMenuOpen(prev => !prev);
  }, []);

  const toggleSection = useCallback((section: Section, isOpen?: boolean) => {
    setOpenSections(prev => ({
      ...prev,
      [section]: isOpen !== undefined ? isOpen : !prev[section],
    }));
  }, []);

  return (
    <div>
      <Tooltip tooltip="Trips View">
        <FloatingButton onClick={toggleMainMenu} src={icon} aria-label="Toggle Trips View Menu" />
      </Tooltip>

      {mainMenuOpen && (
        <div
          draggable={true}
          className={styles.panel}
        >
          <header className={styles.header}>
            <h2>Trips View</h2>
          </header>
          <div className={styles.buttonRow}>
            {sections.map(({ name }) => (
              <Button
                key={name}
                variant='flat'
                aria-label={name}
                aria-expanded={openSections[name]}
                className={
                  openSections[name] ? styles.buttonSelected : styles.TripsDataViewButton
                }
                onClick={() => toggleSection(name)}
                onMouseDown={(e) => e.preventDefault()}
              >
                {name}
              </Button>
            ))}
          </div>
        </div>
      )}

      {sections.map(({ name, component: Component }) => (
        openSections[name] && (
          <Component key={name} onClose={() => toggleSection(name, false)} />
        )
      ))}
    </div>
  );
};

export default TripsDataViewButton;
