import { ApplicationConfig, provideZoneChangeDetection } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { AbstractSecurityStorage, authInterceptor, provideAuth } from 'angular-auth-oidc-client';
import { provideEchartsCore } from 'ngx-echarts';
import * as echarts from 'echarts/core';
import { BarChart, LineChart, PieChart } from 'echarts/charts';
import { GridComponent, LegendComponent, TooltipComponent } from 'echarts/components';
import { CanvasRenderer } from 'echarts/renderers';

import { routes } from './app.routes';
import { authConfig } from './core/auth/auth.config';
import { MemoryStorage } from './core/auth/memory-storage';

echarts.use([BarChart, LineChart, PieChart, GridComponent, LegendComponent, TooltipComponent, CanvasRenderer]);

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withInterceptors([authInterceptor()])),
    provideAnimationsAsync(),
    provideAuth({ config: authConfig }),
    // keep tokens out of session/local storage
    { provide: AbstractSecurityStorage, useClass: MemoryStorage },
    provideEchartsCore({ echarts })
  ]
};
