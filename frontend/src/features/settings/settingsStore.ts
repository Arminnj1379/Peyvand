import { create } from 'zustand';
import { apiClient } from '../../shared/api/client';

export type Theme = 'system' | 'light' | 'dark';

interface SettingsDto {
  theme: Theme;
  notificationsEnabled: boolean;
}

function applyTheme(theme: Theme): void {
  if (theme === 'system') {
    delete document.documentElement.dataset.theme;
  } else {
    document.documentElement.dataset.theme = theme;
  }
}

interface SettingsState {
  theme: Theme;
  notificationsEnabled: boolean;
  loaded: boolean;
  load: () => Promise<void>;
  update: (theme: Theme, notificationsEnabled: boolean) => Promise<void>;
}

export const useSettingsStore = create<SettingsState>((set) => ({
  theme: 'system',
  notificationsEnabled: true,
  loaded: false,

  load: async () => {
    try {
      const dto = await apiClient.get<SettingsDto>('/api/v1/users/me/settings');
      applyTheme(dto.theme);
      set({ theme: dto.theme, notificationsEnabled: dto.notificationsEnabled, loaded: true });
    } catch {
      // Offline or signed out: OS theme default, notifications on.
      applyTheme('system');
      set({ loaded: true });
    }
  },

  update: async (theme, notificationsEnabled) => {
    const dto = await apiClient.patch<SettingsDto>('/api/v1/users/me/settings', {
      theme,
      notificationsEnabled,
    });
    applyTheme(dto.theme);
    set({ theme: dto.theme, notificationsEnabled: dto.notificationsEnabled });
  },
}));
