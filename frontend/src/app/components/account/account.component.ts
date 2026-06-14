import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService, Tier } from '../../services/auth.service';

@Component({
  selector: 'app-account',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './account.component.html',
  styleUrl: './account.component.scss',
})
export class AccountComponent {
  protected readonly auth = inject(AuthService);
  protected readonly mode = signal<'login' | 'register'>('login');
  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly error = signal<string | null>(null);
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

  protected changeTier(tier: Tier): void {
    this.auth.changeTier(tier).subscribe();
  }
}
