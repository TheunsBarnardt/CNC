import { useState } from "react";
import { LayoutGrid, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { projectApi, type ProjectDto } from "@/lib/project";

const ROTATION_OPTIONS = [
  { value: 0, label: "No rotation" },
  { value: 90, label: "90° steps" },
  { value: 45, label: "45° steps" },
  { value: 180, label: "180° steps" },
];

interface Props {
  hasParts: boolean;
  /** Receives the re-arranged project after a successful nest. */
  onNested: (project: ProjectDto) => void;
}

/**
 * Auto-nest action: margin/spacing/rotation settings + the Nest button.
 * The backend repositions parts through the same transform manual dragging
 * uses, so the result can be hand-adjusted afterwards.
 */
export function NestCard({ hasParts, onNested }: Props) {
  const [margin, setMargin] = useState("10");
  const [spacing, setSpacing] = useState("5");
  const [rotationStep, setRotationStep] = useState(90);
  const [busy, setBusy] = useState(false);
  const [summary, setSummary] = useState<string | null>(null);
  const [warnings, setWarnings] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);

  const marginN = Number(margin);
  const spacingN = Number(spacing);
  const invalid =
    !Number.isFinite(marginN) || marginN < 0 ||
    !Number.isFinite(spacingN) || spacingN < 0;

  const nest = async () => {
    setBusy(true);
    setError(null);
    setSummary(null);
    setWarnings([]);
    try {
      const result = await projectApi.nest({
        marginMm: marginN,
        spacingMm: spacingN,
        rotationStepDeg: rotationStep,
      });
      onNested(result.project);
      setSummary(
        `${result.placedCount} part${result.placedCount === 1 ? "" : "s"} placed` +
          (result.skippedCount > 0 ? `, ${result.skippedCount} skipped` : ""),
      );
      setWarnings(result.warnings);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="grid gap-3">
      <div className="grid grid-cols-2 gap-2">
        <div className="grid gap-1">
          <Label className="text-xs" htmlFor="nest-margin">Sheet margin (mm)</Label>
          <Input
            id="nest-margin"
            className="h-8 text-xs"
            inputMode="decimal"
            value={margin}
            onChange={(e) => setMargin(e.target.value)}
          />
        </div>
        <div className="grid gap-1">
          <Label className="text-xs" htmlFor="nest-spacing">Part spacing (mm)</Label>
          <Input
            id="nest-spacing"
            className="h-8 text-xs"
            inputMode="decimal"
            value={spacing}
            onChange={(e) => setSpacing(e.target.value)}
          />
        </div>
      </div>

      <div className="grid gap-1">
        <Label className="text-xs">Rotation</Label>
        <Select
          value={String(rotationStep)}
          onValueChange={(v) => setRotationStep(Number(v))}
        >
          <SelectTrigger className="h-8 text-xs">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {ROTATION_OPTIONS.map((o) => (
              <SelectItem key={o.value} value={String(o.value)}>
                {o.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <Button
        size="sm"
        onClick={() => void nest()}
        disabled={busy || !hasParts || invalid}
      >
        {busy ? <Loader2 className="size-4 animate-spin" /> : <LayoutGrid className="size-4" />}
        Nest parts
      </Button>

      {!hasParts && (
        <p className="text-xs text-muted-foreground">
          Place at least one part on the table first.
        </p>
      )}
      {summary && <p className="text-xs text-muted-foreground">{summary}</p>}
      {warnings.map((w) => (
        <div
          key={w}
          className="rounded-md border border-yellow-600/50 bg-yellow-500/10 px-3 py-1.5 text-xs text-yellow-700 dark:text-yellow-400"
        >
          {w}
        </div>
      ))}
      {error && (
        <div className="rounded-md border border-destructive/50 bg-destructive/10 px-3 py-1.5 text-xs text-destructive">
          {error}
        </div>
      )}
    </div>
  );
}
