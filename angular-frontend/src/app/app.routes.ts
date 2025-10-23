import { Routes } from '@angular/router';
import { roleGuard } from './core/auth/role.guard';

export const routes: Routes = [
  { path: '', loadComponent: () => import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent) },
  { path: 'callback', loadComponent: () => import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent) },
  {
    path: 'claims/new',
    canActivate: [roleGuard('Admin', 'ProviderAdmin', 'ProviderBiller')],
    loadComponent: () => import('./features/claims/claim-entry.component').then(m => m.ClaimEntryComponent)
  },
  { path: 'claims/:id/edit', loadComponent: () => import('./features/claims/claim-entry.component').then(m => m.ClaimEntryComponent) },
  { path: 'claims/:id/correct', loadComponent: () => import('./features/claims/claim-entry.component').then(m => m.ClaimEntryComponent) },
  { path: 'claims', loadComponent: () => import('./features/claims/claims-list.component').then(m => m.ClaimsListComponent) },
  { path: 'claims/:id', loadComponent: () => import('./features/claims/claim-detail.component').then(m => m.ClaimDetailComponent) },
  { path: '**', redirectTo: '' }
];
