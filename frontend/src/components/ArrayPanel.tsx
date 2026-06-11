import { useState } from "react";
import { Grid2X2, CircleDot, TestTube2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Separator } from "@/components/ui/separator";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover";

type Tab = "grid" | "circular" | "test";

interface GridParams {
  rows: number; cols: number;
  spacingXMm: number; spacingYMm: number;
}

interface CircularParams {
  count: number; radiusMm: number;
  startAngleDeg: number; rotateWithArray: boolean;
}

interface TestParams {
  rows: number; cols: number; spacingMm: number;
  param1Type: string; param1Min: number; param1Max: number;
  param2Type: string; param2Min: number; param2Max: number;
}

interface Props {
  onArray: (
    type: "grid" | "circular",
    params: GridParams | CircularParams,
  ) => void;
  onTestArray: (params: TestParams) => void;
}

const PARAM_TYPES = ["feed", "pierce", "power"];

function numField(
  label: string,
  value: number,
  onChange: (n: number) => void,
  opts?: { min?: number; step?: number; width?: string },
) {
  return (
    <label className="flex items-center justify-between gap-2 text-xs">
      <span className="text-muted-foreground">{label}</span>
      <Input
        className={`h-7 ${opts?.width ?? "w-[72px]"} px-1.5 text-xs`}
        type="number"
        min={opts?.min ?? 1}
        step={opts?.step ?? 1}
        value={value}
        onChange={(e) => {
          const n = Number(e.target.value);
          if (Number.isFinite(n)) onChange(n);
        }}
      />
    </label>
  );
}

function selectField(
  label: string,
  value: string,
  onChange: (v: string) => void,
) {
  return (
    <label className="flex items-center justify-between gap-2 text-xs">
      <span className="text-muted-foreground">{label}</span>
      <select
        className="h-7 rounded-md border border-input bg-background px-1.5 text-xs"
        value={value}
        onChange={(e) => onChange(e.target.value)}
      >
        {PARAM_TYPES.map((t) => (
          <option key={t} value={t}>{t}</option>
        ))}
      </select>
    </label>
  );
}

export function ArrayPanel({ onArray, onTestArray }: Props) {
  const [open, setOpen] = useState(false);
  const [tab, setTab] = useState<Tab>("grid");

  const [grid, setGrid] = useState<GridParams>({
    rows: 2, cols: 2, spacingXMm: 10, spacingYMm: 10,
  });
  const [circ, setCirc] = useState<CircularParams>({
    count: 6, radiusMm: 50, startAngleDeg: 0, rotateWithArray: false,
  });
  const [test, setTest] = useState<TestParams>({
    rows: 3, cols: 3, spacingMm: 10,
    param1Type: "feed",  param1Min: 1000, param1Max: 3000,
    param2Type: "pierce", param2Min: 0,    param2Max: 1,
  });

  const applyGrid = () => {
    onArray("grid", grid);
    setOpen(false);
  };

  const applyCircular = () => {
    onArray("circular", circ);
    setOpen(false);
  };

  const applyTest = () => {
    onTestArray(test);
    setOpen(false);
  };

  const tabBtn = (t: Tab, icon: React.ReactNode, title: string) => (
    <button
      className={`flex items-center gap-1 rounded px-2 py-1 text-xs transition-colors ${
        tab === t
          ? "bg-primary text-primary-foreground"
          : "text-muted-foreground hover:bg-accent"
      }`}
      onClick={() => setTab(t)}
    >
      {icon}
      {title}
    </button>
  );

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button variant="ghost" size="icon" className="size-7" title="Array / test grid">
          <Grid2X2 className="size-4" />
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-64 p-3" align="center" side="bottom">
        {/* Tab bar */}
        <div className="mb-3 flex gap-1">
          {tabBtn("grid",     <Grid2X2 className="size-3" />, "Grid")}
          {tabBtn("circular", <CircleDot className="size-3" />, "Circular")}
          {tabBtn("test",     <TestTube2 className="size-3" />, "Test")}
        </div>
        <Separator className="mb-3" />

        {tab === "grid" && (
          <div className="flex flex-col gap-2">
            {numField("Rows", grid.rows, (v) => setGrid((g) => ({ ...g, rows: v })), { min: 1 })}
            {numField("Cols", grid.cols, (v) => setGrid((g) => ({ ...g, cols: v })), { min: 1 })}
            {numField("X spacing (mm)", grid.spacingXMm, (v) => setGrid((g) => ({ ...g, spacingXMm: v })), { min: 0, step: 0.5 })}
            {numField("Y spacing (mm)", grid.spacingYMm, (v) => setGrid((g) => ({ ...g, spacingYMm: v })), { min: 0, step: 0.5 })}
            <Button size="sm" className="mt-1 h-7 w-full text-xs" onClick={applyGrid}>
              Create {grid.rows * grid.cols} copies
            </Button>
          </div>
        )}

        {tab === "circular" && (
          <div className="flex flex-col gap-2">
            {numField("Count", circ.count, (v) => setCirc((c) => ({ ...c, count: v })), { min: 2 })}
            {numField("Radius (mm)", circ.radiusMm, (v) => setCirc((c) => ({ ...c, radiusMm: v })), { min: 1, step: 5 })}
            {numField("Start angle (°)", circ.startAngleDeg, (v) => setCirc((c) => ({ ...c, startAngleDeg: v })), { min: 0, step: 15 })}
            <label className="flex items-center gap-2 text-xs">
              <input
                type="checkbox"
                checked={circ.rotateWithArray}
                onChange={(e) => setCirc((c) => ({ ...c, rotateWithArray: e.target.checked }))}
              />
              <span className="text-muted-foreground">Rotate copies with array</span>
            </label>
            <Button size="sm" className="mt-1 h-7 w-full text-xs" onClick={applyCircular}>
              Create {circ.count} copies
            </Button>
          </div>
        )}

        {tab === "test" && (
          <div className="flex flex-col gap-2">
            <p className="text-[11px] text-muted-foreground">
              Grid of test cuts sweeping two parameters (e.g. feed × pierce delay).
            </p>
            {numField("Rows", test.rows, (v) => setTest((t) => ({ ...t, rows: v })), { min: 1 })}
            {numField("Cols", test.cols, (v) => setTest((t) => ({ ...t, cols: v })), { min: 1 })}
            {numField("Spacing (mm)", test.spacingMm, (v) => setTest((t) => ({ ...t, spacingMm: v })), { min: 1, step: 5 })}
            <Separator />
            {selectField("Row param", test.param1Type, (v) => setTest((t) => ({ ...t, param1Type: v })))}
            {numField("Min", test.param1Min, (v) => setTest((t) => ({ ...t, param1Min: v })), { min: 0, step: 100 })}
            {numField("Max", test.param1Max, (v) => setTest((t) => ({ ...t, param1Max: v })), { min: 0, step: 100 })}
            <Separator />
            {selectField("Col param", test.param2Type, (v) => setTest((t) => ({ ...t, param2Type: v })))}
            {numField("Min", test.param2Min, (v) => setTest((t) => ({ ...t, param2Min: v })), { min: 0, step: 0.1 })}
            {numField("Max", test.param2Max, (v) => setTest((t) => ({ ...t, param2Max: v })), { min: 0, step: 0.1 })}
            <Button size="sm" className="mt-1 h-7 w-full text-xs" onClick={applyTest}>
              Download {test.rows * test.cols}-cell G-code
            </Button>
          </div>
        )}
      </PopoverContent>
    </Popover>
  );
}
