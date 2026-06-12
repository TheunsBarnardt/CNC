import { useState } from "react";

export type RulerUnit = "mm" | "cm" | "m" | "in" | "ft";

export interface AppSettings {
  showRulers: boolean;
  showGuides: boolean;
  snapToGuides: boolean;
  mouseWheelPans: boolean;
  rulerUnit: RulerUnit;
}

const STORAGE_KEY = "grblcam-settings";
const DEFAULTS: AppSettings = {
  showRulers: true,
  showGuides: true,
  snapToGuides: true,
  mouseWheelPans: false,
  rulerUnit: "mm",
};

export function useSettings() {
  const [settings, setSettings] = useState<AppSettings>(() => {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      return raw ? { ...DEFAULTS, ...(JSON.parse(raw) as Partial<AppSettings>) } : DEFAULTS;
    } catch {
      return DEFAULTS;
    }
  });

  const update = (patch: Partial<AppSettings>) => {
    setSettings((prev) => {
      const next = { ...prev, ...patch };
      localStorage.setItem(STORAGE_KEY, JSON.stringify(next));
      return next;
    });
  };

  return { settings, update };
}
