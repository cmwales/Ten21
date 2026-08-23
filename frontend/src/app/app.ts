import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ToastHost } from './shared/toast-host/toast-host';

@Component({
  imports: [RouterOutlet, ToastHost],
  selector: 'app-root',
  styles: [],
  templateUrl: './app.html',
})
export class App {}
