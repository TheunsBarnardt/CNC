import { useCallback, useEffect, useRef, useState } from "react";
import { Loader2, Pause, Play, RefreshCw, Square, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { formatTime, type Simulation } from "@/lib/simulation";

const SPEEDS = [0.5, 1, 2, 4, 8, 16];

interface SimulationBarProps {
  simulation: Simulation | null;
  simTime: number;
  onSimTimeChange: (t: number) => void;
  /** Fetch the toolpath and (re)build the simulation. */
  onGenerate: () => Promise<Simulation | null>;
  onClose: () => void;
  hasParts: boolean;
}

/**
 * Playback controls for the toolpath simulation: play/pause, scrub, speed.
 * Purely visual — this never sends anything to a machine.
 */
export function SimulationBar({
  simulation,
  simTime,
  onSimTimeChange,
  onGenerate,
  onClose,
  hasParts,
}: SimulationBarProps) {
  const [playing, setPlaying] = useState(false);
  const [speed, setSpeed] = useState(2);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const rafRef = useRef(0);
  const lastTickRef = useRef(0);
  // Refs so the rAF loop reads fresh values without re-subscribing each frame.
  const timeRef = useRef(simTime);
  timeRef.current = simTime;
  const speedRef = useRef(speed);
  speedRef.current = speed;

  useEffect(() => {
    if (!playing || !simulation) return;
    lastTickRef.current = performance.now();
    const tick = (now: number) => {
      const dt = (now - lastTickRef.current) / 1000;
      lastTickRef.current = now;
      const t = timeRef.current + dt * speedRef.current;
      if (t >= simulation.totalS) {
        onSimTimeChange(simulation.totalS);
        setPlaying(false);
        return;
      }
      onSimTimeChange(t);
      rafRef.current = requestAnimationFrame(tick);
    };
    rafRef.current = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(rafRef.current);
  }, [playing, simulation, onSimTimeChange]);

  const generate = useCallback(async () => {
    setBusy(true);
    setError(null);
    setPlaying(false);
    try {
      const sim = await onGenerate();
      onSimTimeChange(0);
      if (sim && sim.segments.length > 0) setPlaying(true);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  }, [onGenerate, onSimTimeChange]);

  // No simulation yet: a single entry point.
  if (!simulation) {
    return (
      <div className="flex items-center gap-3 rounded-lg border bg-card px-3 py-2">
        <Button size="sm" onClick={() => void generate()} disabled={busy || !hasParts}>
          {busy ? <Loader2 className="size-4 animate-spin" /> : <Play className="size-4" />}
          Simulate
        </Button>
        <span className="text-xs text-muted-foreground">
          {hasParts
            ? "Preview the cut: torch path, pierces, and rapids, played back like a video."
            : "Place parts on the table to simulate."}
        </span>
        {error && <span className="text-xs text-destructive">{error}</span>}
      </div>
    );
  }

  const atEnd = simTime >= simulation.totalS;

  return (
    <div className="flex items-center gap-2 rounded-lg border bg-card px-3 py-2">
      <Button
        variant="secondary"
        size="icon"
        className="size-8"
        title={playing ? "Pause" : "Play"}
        onClick={() => {
          if (atEnd) onSimTimeChange(0);
          setPlaying((p) => !p);
        }}
      >
        {playing ? <Pause className="size-4" /> : <Play className="size-4" />}
      </Button>
      <Button
        variant="ghost"
        size="icon"
        className="size-8"
        title="Stop (back to start)"
        onClick={() => {
          setPlaying(false);
          onSimTimeChange(0);
        }}
      >
        <Square className="size-4" />
      </Button>

      <span className="w-[88px] shrink-0 text-center font-mono text-xs text-muted-foreground">
        {formatTime(simTime)} / {formatTime(simulation.totalS)}
      </span>

      <input
        type="range"
        className="h-1.5 min-w-0 flex-1 cursor-pointer accent-primary"
        min={0}
        max={simulation.totalS}
        step={simulation.totalS / 1000}
        value={Math.min(simTime, simulation.totalS)}
        onChange={(e) => {
          setPlaying(false);
          onSimTimeChange(Number(e.target.value));
        }}
      />

      <Select value={String(speed)} onValueChange={(v) => setSpeed(Number(v))}>
        <SelectTrigger size="sm" className="h-8 w-[76px] text-xs">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {SPEEDS.map((s) => (
            <SelectItem key={s} value={String(s)}>
              {s}×
            </SelectItem>
          ))}
        </SelectContent>
      </Select>

      <span className="hidden shrink-0 text-xs text-muted-foreground lg:inline">
        {simulation.cutCount} cuts · {simulation.totalCutLengthMm.toFixed(0)}mm
      </span>

      <Button
        variant="ghost"
        size="icon"
        className="size-8"
        title="Regenerate toolpath (after moving parts or changing settings)"
        onClick={() => void generate()}
        disabled={busy}
      >
        {busy ? <Loader2 className="size-4 animate-spin" /> : <RefreshCw className="size-4" />}
      </Button>
      <Button
        variant="ghost"
        size="icon"
        className="size-8"
        title="Close simulation"
        onClick={() => {
          setPlaying(false);
          onClose();
        }}
      >
        <X className="size-4" />
      </Button>
    </div>
  );
}
