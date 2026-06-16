import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { AuthService, Tier } from '../../services/auth.service';
import { VesselService } from '../../services/vessel.service';

@Component({
  selector: 'app-account',
  standalone: true,
  imports: [
    FormsModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatFormFieldModule,
    MatInputModule,
    MatIconModule,
    MatChipsModule,
  ],
  templateUrl: './account.component.html',
  styleUrl: './account.component.scss',
})
export class AccountComponent {
  protected readonly auth = inject(AuthService);
  protected readonly vesselService = inject(VesselService);
  protected readonly mode = signal<'login' | 'register' | 'forgot'>('login');
  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly error = signal<string | null>(null);
  protected readonly info = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly tiers: Tier[] = ['Free', 'Pro', 'Enterprise'];

  protected submit(): void {
    this.error.set(null);
    this.busy.set(true);
    const action =
      this.mode() === 'login'
        ? this.auth.login(this.email(), this.password())
        : this.auth.register(this.email(), this.password());

    action.subscribe({
      next: () => {
        this.busy.set(false);
        this.password.set('');
      },
      error: (err) => {
        this.busy.set(false);
        const body = err?.error;
        this.error.set(body?.error ?? body?.errors?.[0] ?? 'Authentication failed.');
      },
    });
  }

  protected sendReset(): void {
    this.error.set(null);
    this.info.set(null);
    this.busy.set(true);
    this.auth.forgotPassword(this.email()).subscribe({
      next: (r) => {
        this.busy.set(false);
        this.info.set(r.message);
      },
      error: () => {
        this.busy.set(false);
        this.info.set('If that email exists, a reset link has been sent.');
      },
    });
  }

  protected changeTier(tier: Tier): void {
    this.auth.changeTier(tier).subscribe();
  }
}
