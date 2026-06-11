import { Settings } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import { Separator } from "@/components/ui/separator";
import type { AppSettings } from "@/lib/settings";

interface Props {
  settings: AppSettings;
  onUpdate: (patch: Partial<AppSettings>) => void;
}

function Row({
  label,
  description,
  checked,
  onChange,
}: {
  label: string;
  description?: string;
  checked: boolean;
  onChange: (v: boolean) => void;
}) {
  return (
    <label className="flex cursor-pointer items-start gap-3 py-1.5">
      <input
        type="checkbox"
        className="mt-0.5 h-4 w-4 accent-primary"
        checked={checked}
        onChange={(e) => onChange(e.target.checked)}
      />
      <div className="flex flex-col">
        <span className="text-sm">{label}</span>
        {description && (
          <span className="text-xs text-muted-foreground">{description}</span>
        )}
      </div>
    </label>
  );
}

export function SettingsDialog({ settings, onUpdate }: Props) {
  return (
    <Dialog>
      <DialogTrigger asChild>
        <Button variant="ghost" size="icon" className="size-8" title="Settings">
          <Settings className="size-4" />
        </Button>
      </DialogTrigger>
      <DialogContent className="max-w-sm">
        <DialogHeader>
          <DialogTitle>Settings</DialogTitle>
        </DialogHeader>

        <div className="flex flex-col gap-1">
          <p className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
            Canvas
          </p>
          <Row
            label="Show rulers"
            description="Millimetre rulers along the top and left edges"
            checked={settings.showRulers}
            onChange={(v) => onUpdate({ showRulers: v })}
          />
          <Row
            label="Show guide lines"
            description="Drag from a ruler to create a guide line"
            checked={settings.showGuides}
            onChange={(v) => onUpdate({ showGuides: v })}
          />
          <Row
            label="Snap to guides"
            description="Part edges snap to guide lines when dragging"
            checked={settings.snapToGuides}
            onChange={(v) => onUpdate({ snapToGuides: v })}
          />

          <Separator className="my-2" />

          <p className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
            Mouse wheel
          </p>
          <Row
            label="Wheel pans canvas"
            description="Off = wheel zooms (same as Ctrl+scroll when off)"
            checked={settings.mouseWheelPans}
            onChange={(v) => onUpdate({ mouseWheelPans: v })}
          />
        </div>
      </DialogContent>
    </Dialog>
  );
}
