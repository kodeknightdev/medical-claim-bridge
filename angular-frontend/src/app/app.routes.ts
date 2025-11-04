import { Routes } from '@angular/router';
import { roleGuard } from './core/auth/role.guard';

export const routes: Routes = [
  { path: '', loadComponent: () => import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent) },
  { path: 'callback', loadComponent: () => import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent) },
  { path: 'claims', loadComponent: () => import('./features/claims/claims-list.component').then(m => m.ClaimsListComponent) },
  {
    path: 'claims/new',
    canActivate: [roleGuard('Admin', 'ProviderAdmin', 'ProviderBiller')],
    loadComponent: () => import('./features/claims/claim-entry.component').then(m => m.ClaimEntryComponent)
  },
  { path: 'claims/:id/edit', loadComponent: () => import('./features/claims/claim-entry.component').then(m => m.ClaimEntryComponent) },
  { path: 'claims/:id/correct', loadComponent: () => import('./features/claims/claim-entry.component').then(m => m.ClaimEntryComponent) },
  { path: 'claims/:id', loadComponent: () => import('./features/claims/claim-detail.component').then(m => m.ClaimDetailComponent) },
  { path: 'batches', loadComponent: () => import('./features/batches/batches.component').then(m => m.BatchesComponent) },
  { path: 'eligibility', loadComponent: () => import('./features/eligibility/eligibility.component').then(m => m.EligibilityComponent) },
  { path: 'denials', loadComponent: () => import('./features/denials/denials.component').then(m => m.DenialsComponent) },
  { path: 'remittances', loadComponent: () => import('./features/remittances/remittances.component').then(m => m.RemittancesComponent) },
  { path: 'remittances/:id', loadComponent: () => import('./features/remittances/remittance-detail.component').then(m => m.RemittanceDetailComponent) },
  { path: 'enrollment', loadComponent: () => import('./features/enrollment/enrollment.component').then(m => m.EnrollmentComponent) },
  {
    path: 'integrations',
    canActivate: [roleGuard('Admin', 'OpsAnalyst', 'ProviderAdmin')],
    loadComponent: () => import('./features/integrations/integrations.component').then(m => m.IntegrationsComponent)
  },
  {
    path: 'ops/payers',
    canActivate: [roleGuard('Admin', 'OpsAnalyst')],
    loadComponent: () => import('./features/ops/payer-health.component').then(m => m.PayerHealthComponent)
  },
  {
    path: 'ops/reconciliation',
    canActivate: [roleGuard('Admin', 'OpsAnalyst')],
    loadComponent: () => import('./features/ops/reconciliation.component').then(m => m.ReconciliationComponent)
  },
  { path: '**', redirectTo: '' }
];
