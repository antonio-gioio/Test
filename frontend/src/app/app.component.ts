import { Component, OnInit, inject } from '@angular/core';
import { MapComponent } from './components/map/map.component';
import { SidebarComponent } from './components/sidebar/sidebar.component';
import { VesselService } from './services/vessel.service';

@Component({
  selector: 'app-root',
  imports: [MapComponent, SidebarComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
})
export class AppComponent implements OnInit {
  protected readonly vesselService = inject(VesselService);

  ngOnInit(): void {
    this.vesselService.start();
  }
}
