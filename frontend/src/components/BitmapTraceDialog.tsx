import { useEffect, useState } from "react";
import { ImageDown, RefreshCw } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import { Separator } from "@/components/ui/separator";
import type { BitmapTraceSettings, FileSummary } from "@/lib/project";
import { BACKEND_URL } from "@/lib/backend";

interface Props {
  file: FileSummary;
  onRetrace: (settings: BitmapTraceSettings) => Promise<void>;
}

function parseSettings(json: string | null): BitmapTraceSettings {
  if (!json) return defaults();
  try {
    const raw = JSON.parse(json) as Record<string, unknown>;
    return {
      thresholdPercent: Number(raw.thresholdPercent ?? raw.ThresholdPercent ?? 50),
      invert: Boolean(raw.invert ?? raw.Invert ?? false),
      brightness: Number(raw.brightness ?? raw.Brightness ?? 0),
      contrast: Number(raw.contrast ?? raw.Contrast ?? 1),
      mode: (String(raw.mode ?? raw.Mode ?? "outline").toLowerCase() as "outline" | "centerline"),
      simplifyToleranceMm: Number(raw.simplifyToleranceMm ?? raw.SimplifyToleranceMm ?? 0.3),
    };
  } catch {
    return defaults();
  }
}

function defaults(): BitmapTraceSettings {
  return {
    thresholdPercent: 50,
    invert: false,
    brightness: 0,
    contrast: 1,
    mode: "outline",
    simplifyToleranceMm: 0.3,
  };
}

export function BitmapTraceDialog({ file, onRetrace }: Props) {
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [settings, setSettings] = useState<BitmapTraceSettings>(
    () => parseSettings(file.bitmapTraceSettingsJson)
  );

  // Reset settings when dialog opens
  useEffect(() => {
    if (open) setSettings(parseSettings(file.bitmapTraceSettingsJson));
  }, [open, file.bitmapTraceSettingsJson]);

  const patch = (partial: Partial<BitmapTraceSettings>) =>
    setSettings((s) => ({ ...s, ...partial }));

  const handleRetrace = async () => {
    setBusy(true);
    try {
      await onRetrace(settings);
      setOpen(false);
    } finally {
      setBusy(false);
    }
  };

  const imageUrl = `${BACKEND_URL}/api/project/files/${file.id}/bitmap-image`;

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button variant="ghost" size="icon" className="size-6" title="Trace settings">
          <ImageDown className="size-3" />
        </Button>
      </DialogTrigger>

      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>Bitmap Trace — {file.displayName}</DialogTitle>
        </DialogHeader>

        <div className="flex gap-4">
          {/* Image preview */}
          <div className="flex-shrink-0">
            <img
              src={imageUrl}
              alt="bitmap preview"
              className="h-32 w-32 rounded border border-border object-contain"
              style={{ background: "repeating-conic-gradient(#e5e7eb 0% 25%, #fff 0% 50%) 0 0 / 12px 12px" }}
            />
          </div>

          {/* Settings */}
          <div className="flex flex-1 flex-col gap-3 text-sm">
            {/* Mode */}
            <div className="flex flex-col gap-1">
              <span className="text-xs font-medium uppercase tracking-wider text-muted-foreground">Mode</span>
              <div className="flex gap-2">
                {(["outline", "centerline"] as const).map((m) => (
                  <button
                    key={m}
                    onClick={() => patch({ mode: m })}
                    className={`rounded px-3 py-1 text-xs capitalize border ${
                      settings.mode === m
                        ? "bg-primary text-primary-foreground border-primary"
                        : "border-border hover:bg-accent"
                    }`}
                  >
                    {m}
                  </button>
                ))}
              </div>
            </div>

            {/* Threshold */}
            <label className="flex flex-col gap-1">
              <span className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
                Threshold — {settings.thresholdPercent}%
              </span>
              <input
                type="range" min={1} max={99} step={1}
                value={settings.thresholdPercent}
                onChange={(e) => patch({ thresholdPercent: Number(e.target.value) })}
                className="accent-primary"
              />
            </label>

            {/* Brightness */}
            <label className="flex flex-col gap-1">
              <span className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
                Brightness — {settings.brightness > 0 ? "+" : ""}{(settings.brightness * 100).toFixed(0)}%
              </span>
              <input
                type="range" min={-100} max={100} step={1}
                value={Math.round(settings.brightness * 100)}
                onChange={(e) => patch({ brightness: Number(e.target.value) / 100 })}
                className="accent-primary"
              />
            </label>

            {/* Contrast */}
            <label className="flex flex-col gap-1">
              <span className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
                Contrast — {settings.contrast.toFixed(1)}×
              </span>
              <input
                type="range" min={10} max={300} step={5}
                value={Math.round(settings.contrast * 100)}
                onChange={(e) => patch({ contrast: Number(e.target.value) / 100 })}
                className="accent-primary"
              />
            </label>

            <Separator />

            <div className="flex items-center gap-3">
              {/* Invert */}
              <label className="flex cursor-pointer items-center gap-1.5 text-xs">
                <input
                  type="checkbox"
                  className="h-3.5 w-3.5 accent-primary"
                  checked={settings.invert}
                  onChange={(e) => patch({ invert: e.target.checked })}
                />
                Invert
              </label>

              {/* Simplify tolerance */}
              <label className="flex items-center gap-1.5 text-xs">
                <span className="text-muted-foreground">Simplify</span>
                <input
                  type="number"
                  className="w-16 rounded border border-border bg-background px-1 py-0.5 text-xs"
                  min={0.05} max={5} step={0.05}
                  value={settings.simplifyToleranceMm}
                  onChange={(e) => patch({ simplifyToleranceMm: Number(e.target.value) })}
                />
                <span className="text-muted-foreground">mm</span>
              </label>
            </div>
          </div>
        </div>

        <div className="flex justify-end gap-2 pt-2">
          <Button variant="outline" onClick={() => setOpen(false)}>
            Cancel
          </Button>
          <Button onClick={handleRetrace} disabled={busy}>
            {busy ? (
              <RefreshCw className="mr-2 size-4 animate-spin" />
            ) : (
              <RefreshCw className="mr-2 size-4" />
            )}
            Re-trace
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}
