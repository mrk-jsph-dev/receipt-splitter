import { createRouter, createWebHistory, type RouterHistory } from 'vue-router';
import BillShell from './bill/BillShell.vue';
import ItemsView from './bill/ItemsView.vue';
import PeopleView from './bill/PeopleView.vue';
import ReceiptsView from './bill/ReceiptsView.vue';
import SettleView from './bill/SettleView.vue';
import ReceiptEditor from './bill/edit/ReceiptEditor.vue';
import ShareView from './bill/edit/ShareView.vue';
import HomeView from './home/HomeView.vue';
import NameView from './profile/NameView.vue';
import ScanView from './scan/ScanView.vue';
import WelcomeView from './welcome/WelcomeView.vue';
import { needsWelcome } from './welcome/welcome';

export function createAppRouter(history: RouterHistory = createWebHistory()) {
  return createRouter({
    history,
    routes: [
      { path: '/', component: HomeView, beforeEnter: () => (needsWelcome() ? '/welcome' : true) },
      { path: '/welcome', component: WelcomeView },
      { path: '/name', component: NameView },
      { path: '/scan', component: ScanView },
      {
        path: '/b/:id',
        component: BillShell,
        props: true,
        children: [
          { path: '', name: 'items', component: ItemsView },
          { path: 'people', name: 'people', component: PeopleView },
          { path: 'receipts', name: 'receipts', component: ReceiptsView },
          { path: 'receipts/:receiptId', name: 'receipt', component: ReceiptEditor, props: true },
          { path: 'settle', name: 'settle', component: SettleView },
          { path: 'share', name: 'share', component: ShareView },
        ],
      },
      { path: '/:rest(.*)*', redirect: '/' },
    ],
    scrollBehavior: (_to, _from, saved) => saved ?? { top: 0 },
  });
}
