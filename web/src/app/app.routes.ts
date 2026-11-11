import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./features/chat/chat-page').then((m) => m.ChatPageComponent),
  },
];