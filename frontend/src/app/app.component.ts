import { Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatToolbarModule } from '@angular/material/toolbar';
import { AccountComponent } from './components/account/account.component';
import { AdminPanelComponent } from './components/admin/admin-panel.component';
import { AlertToastsComponent } from './components/alerts/alert-toasts.component';
import { MapComponent } from './components/map/map.component';
import { PlaybackComponent } from './components/playback/playback.component';
import { SidebarComponent } from './components/sidebar/sidebar.component';
import { ToolsPanelComponent } from './components/tools/tools-panel.component';
import { AuthService } from './services/auth.service';
import { VesselService } from './services/vessel.service';

@Component({
  selector: 'app-root',
  imports: [
    MapComponent,
    SidebarComponent,
    AccountComponent,
    ToolsPanelComponent,
    AlertToastsComponent,
    AdminPanelComponent,
    PlaybackComponent,
    MatToolbarModule,
    MatButtonModule,
    MatIconModule,
  ],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
})
export class AppComponent implements OnInit {
  protected readonly vesselService = inject(VesselService);
  protected readonly auth = inject(AuthService);
  protected readonly showAdmin = signal(false);
  protected readonly sidebarOpen = signal(false);

  ngOnInit(): void {
    this.vesselService.start();
  }
}
