import { ModRegistrar } from "cs2/modding";
import TripsDataViewMenu from "./mods/TripsDataViewContent/TripsDataViewMenu";
import 'intl';
import 'intl/locale-data/jsonp/en-US'; 

const register: ModRegistrar = (moduleRegistry) => {

    moduleRegistry.append('GameTopLeft', TripsDataViewMenu);
}

export default register;