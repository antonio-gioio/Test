import { Component, OnInit, inject } from '@angular/core';
import { AccountComponent } from './components/account/account.component';
import { MapComponent } from './components/map/map.component';
import { SidebarComponent } from './components/sidebar/sidebar.component';
import { AuthService } from './services/auth.service';
import { VesselService } from './services/vessel.service';

@Component({
  selector: 'app-root',
  imports: [MapComponent, SidebarComponent, AccountComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
})
export class AppComponent implements OnInit {
  protected readonly vesselService = inject(VesselService);
  protected readonly auth = inject(AuthService);

  ngOnInit(): void {
    this.vesselService.start();
  }
}
