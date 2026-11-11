import { Component, input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-refusal-message',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './refusal-message.html',
  styleUrl: './refusal-message.css',
})
export class RefusalMessageComponent {
  refusal = input.required<string>();
}