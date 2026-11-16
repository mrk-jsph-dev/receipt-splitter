import { ref } from 'vue';
import { getProfileName, recentBills } from '../shared/storage';

/** Set by "Skip for now", so the welcome doesn't come back during this visit. */
export const welcomeSkipped = ref(false);

/** The welcome is only for a browser with no saved name and no recent bills. */
export const needsWelcome = () => !welcomeSkipped.value && getProfileName() === null && recentBills().length === 0;
