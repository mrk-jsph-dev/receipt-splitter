import { createRouter, createWebHistory, type RouterHistory } from 'vue-router';
import BillView from './bill/BillView.vue';
import HomeView from './home/HomeView.vue';
import NameView from './profile/NameView.vue';
import WelcomeView from './welcome/WelcomeView.vue';
import { needsWelcome } from './welcome/welcome';

export function createAppRouter(history: RouterHistory = createWebHistory()) {
  return createRouter({
    history,
    routes: [
      { path: '/', component: HomeView, beforeEnter: () => (needsWelcome() ? '/welcome' : true) },
      { path: '/welcome', component: WelcomeView },
      { path: '/name', component: NameView },
      { path: '/b/:id', component: BillView, props: true },
      { path: '/:rest(.*)*', redirect: '/' },
    ],
  });
}
