import { reactive } from 'vue';

// Photos picked on Home or in a bill wait here while the scan page reads them. In memory only:
// nothing is written to the browser's storage, and a reload clears the tray.

export interface TrayItem {
  key: number;
  file: File;
  /** Joins the photo above it as the next page of a long receipt. */
  nextPage: boolean;
  status: 'waiting' | 'reading' | 'read' | 'failed';
  problem: string;
}

let next = 0;
export const tray = reactive<{ items: TrayItem[]; billId: string | null }>({ items: [], billId: null });

export function fillTray(files: File[], billId: string | null = null) {
  tray.items = files.map((file) => ({ key: ++next, file, nextPage: false, status: 'waiting', problem: '' }));
  tray.billId = billId;
}

export function clearTray() {
  tray.items = [];
  tray.billId = null;
}
