import { createApp } from 'vue';
import App from './App.vue';
import './design/base.css';
import { createAppRouter } from './router';

createApp(App).use(createAppRouter()).mount('#root');
