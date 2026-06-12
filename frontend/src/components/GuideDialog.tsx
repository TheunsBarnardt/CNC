import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import type { Guide } from "@/lib/guides";
import type { RulerUnit } from "@/lib/settings";
import { rulerDisplayValue, rulerFormatMm, rulerValueToMm } from "@/lib/project";

interface Props {
  guide: Guide;
  rulerUnit: RulerUnit;
  onSave: (guide: Guide) => void;
  onDelete: () => void;
  onClose: () => void;
}

export function GuideDialog({ guide, rulerUnit, onSave, onDelete, onClose }: Props) {
  const [label, setLabel] = useState(guide.label ?? "");
  const [posText, setPosText] = useState(rulerFormatMm(guide.posMm, rulerUnit));
  const [locked, setLocked] = useState(guide.locked);
  const dialogRef = useRef<HTMLDivElement>(null);

  // Close on Escape.
  useEffect(() => {
    const handler = (e: KeyboardEvent) => { if (e.key === "Escape") onClose(); };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [onClose]);

  // Close on outside click.
  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (dialogRef.current && !dialogRef.current.contains(e.target as Node)) onClose();
    };
    // Delay so the click that opened the dialog doesn't immediately close it.
    const id = setTimeout(() => window.addEventListener("mousedown", handler), 100);
    return () => { clearTimeout(id); window.removeEventListener("mousedown", handler); };
  }, [onClose]);

  const axisLabel = guide.axis === "v" ? "X position" : "Y position";

  const handleSave = () => {
    const raw = Number(posText.replace(",", "."));
    const posMm = Number.isFinite(raw) ? rulerValueToMm(raw, rulerUnit) : guide.posMm;
    onSave({ ...guide, label: label.trim() || undefined, posMm, locked });
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/30">
      <div
        ref={dialogRef}
        className="w-80 rounded-lg border bg-background shadow-xl"
        onMouseDown={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="flex items-center justify-between border-b px-4 py-3">
          <div>
            <p className="text-sm font-semibold">Guideline</p>
            <p className="text-xs text-muted-foreground">
              {guide.axis === "v" ? "Vertical" : "Horizontal"} · ID {guide.id.slice(0, 8)}
            </p>
          </div>
          <button
            className="text-muted-foreground hover:text-foreground text-lg leading-none"
            onClick={onClose}
            aria-label="Close"
          >
            ×
          </button>
        </div>

        {/* Body */}
        <div className="grid gap-4 px-4 py-4">
          <div className="grid gap-1.5">
            <Label htmlFor="guide-label">Label (optional)</Label>
            <Input
              id="guide-label"
              value={label}
              onChange={(e) => setLabel(e.target.value)}
              placeholder="e.g. Fold line"
              autoFocus
            />
          </div>

          <div className="grid gap-1.5">
            <Label htmlFor="guide-pos">
              {axisLabel} ({rulerUnit})
            </Label>
            <Input
              id="guide-pos"
              inputMode="decimal"
              value={posText}
              onChange={(e) => setPosText(e.target.value)}
              onKeyDown={(e) => e.key === "Enter" && handleSave()}
            />
            <p className="text-[10px] text-muted-foreground">
              {rulerDisplayValue(guide.posMm, rulerUnit).toFixed(4)} {rulerUnit} current
            </p>
          </div>

          <div className="flex items-center gap-2">
            <input
              id="guide-locked"
              type="checkbox"
              checked={locked}
              onChange={(e) => setLocked(e.target.checked)}
              className="h-4 w-4 rounded accent-purple-500"
            />
            <Label htmlFor="guide-locked">Locked</Label>
          </div>
        </div>

        {/* Footer */}
        <div className="flex items-center justify-between border-t px-4 py-3">
          <Button variant="destructive" size="sm" onClick={onDelete}>
            Delete
          </Button>
          <div className="flex gap-2">
            <Button variant="outline" size="sm" onClick={onClose}>
              Cancel
            </Button>
            <Button size="sm" onClick={handleSave}>
              OK
            </Button>
          </div>
        </div>
      </div>
    </div>
  );
}
