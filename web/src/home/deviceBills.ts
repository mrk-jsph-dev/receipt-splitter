import { ref } from 'vue';
import { ApiError, getBill, type BillResponse } from '../shared/api';
import { recentBills, rememberBill, setBillValue, type RecentBill } from '../shared/storage';

// "On this device": the bills this browser has opened. The list comes from local storage; the
// numbers on each row come from the API, so the browser never adds money up itself.

export interface DeviceBill extends RecentBill {
  data: BillResponse | null;
}

export function useDeviceBills() {
  const bills = ref<DeviceBill[]>(recentBills().map((b) => ({ ...b, data: null })));

  async function load() {
    await Promise.all(
      bills.value.map(async (row) => {
        try {
          const result = await getBill(row.id);
          if (result.changed) {
            row.data = result.data;
            rememberBill(row.id, result.data.bill.title, new Date(row.openedAt));
          }
        } catch (e) {
          // A deleted or expired bill is gone for everyone, so it leaves this list too.
          if (e instanceof ApiError && e.status === 404) {
            setBillValue(row.id, 'seen', null);
            bills.value = bills.value.filter((b) => b.id !== row.id);
          }
        }
      }),
    );
  }

  function reset() {
    bills.value = [];
  }

  return { bills, load, reset };
}

/** Settled means everyone who owes has been confirmed, or the organizer locked the bill. */
export const isSettled = (data: BillResponse | null) =>
  data !== null && (data.bill.status === 'locked' || (data.breakdown !== null && data.breakdown.progress.owing > 0 && data.breakdown.progress.paidPercent === 100));
