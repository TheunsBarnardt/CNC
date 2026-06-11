/**
 * Left creation rail (xTool Studio anatomy): import + creation tools.
 * Shows all shape tools, pen tool, and text tool.
 */
import { useRef, useState } from "react";
import {
  Circle,
  FileUp,
  Loader2,
  Minus,
  MousePointer2,
  Pentagon,
  PenTool,
  Sparkles,
  Square,
  Type,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover";
import { Separator } from "@/components/ui/separator";
import type { ActiveTool, ShapeOptions, ShapeToolType } from "@/lib/tools";
import { DEFAULT_SHAPE_OPTIONS } from "@/lib/tools";

const ACCEPTED = [".svg", ".dxf"];

interface Props {
  activeTool: ActiveTool;
  onToolChange: (tool: ActiveTool) => void;
  onImport: (files: File[]) => void;
  busy: boolean;
  disabled?: boolean;
}

function ToolBtn({
  active,
  title,
  onClick,
  children,
}: {
  active?: boolean;
  title: string;
  onClick: () => void;
  children: React.ReactNode;
}) {
  return (
    <Button
      variant={active ? "secondary" : "ghost"}
      size="icon"
      className="size-9"
      title={title}
      onClick={onClick}
    >
      {children}
    </Button>
  );
}

/** Compact number input for shape options. */
function NumField({
  label,
  value,
  min,
  max,
  step,
  onChange,
}: {
  label: string;
  value: number;
  min: number;
  max: number;
  step?: number;
  onChange: (v: number) => void;
}) {
  return (
    <div className="flex flex-col gap-0.5">
      <Label className="text-[10px] text-muted-foreground">{label}</Label>
      <Input
        type="number"
        min={min}
        max={max}
        step={step ?? 1}
        value={value}
        onChange={(e) => {
          const v = Number(e.target.value);
          if (Number.isFinite(v)) onChange(Math.min(max, Math.max(min, v)));
        }}
        className="h-7 text-xs"
      />
    </div>
  );
}

export function CreationSidebar({
  activeTool,
  onToolChange,
  onImport,
  busy,
  disabled = false,
}: Props) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [shapeOpts, setShapeOpts] = useState<ShapeOptions>(DEFAULT_SHAPE_OPTIONS);
  const [shapesOpen, setShapesOpen] = useState(false);

  const setTool = (tool: ActiveTool) => {
    if (!disabled) {
      onToolChange(activeTool.type === tool.type ? { type: "select" } : tool);
    }
  };

  const setShapeTool = (type: ShapeToolType) => {
    if (disabled) return;
    if (activeTool.type === type) {
      onToolChange({ type: "select" });
    } else {
      onToolChange({ type, options: shapeOpts });
      setShapesOpen(false);
    }
  };

  const isShape = (t: ShapeToolType) => activeTool.type === t;
  const anyShape = ["line","rect","circle","ellipse","polygon","star"].includes(activeTool.type);

  const updateOpts = (patch: Partial<ShapeOptions>) => {
    const next = { ...shapeOpts, ...patch };
    setShapeOpts(next);
    // If currently using a shape tool, update it with new options.
    if (anyShape) onToolChange({ type: activeTool.type as ShapeToolType, options: next });
  };

  return (
    <div className="flex w-12 shrink-0 flex-col items-center gap-1 border-r bg-card py-2">
      {/* Select */}
      <ToolBtn
        active={activeTool.type === "select"}
        title="Select / move (Escape)"
        onClick={() => onToolChange({ type: "select" })}
      >
        <MousePointer2 className="size-5" />
      </ToolBtn>

      <Separator className="my-1 w-7" />

      {/* Import */}
      <ToolBtn
        title="Import SVG / DXF files"
        onClick={() => inputRef.current?.click()}
      >
        {busy ? <Loader2 className="size-5 animate-spin" /> : <FileUp className="size-5" />}
      </ToolBtn>

      <Separator className="my-1 w-7" />

      {/* Shape tools — grouped in a popover */}
      <Popover open={shapesOpen} onOpenChange={setShapesOpen}>
        <PopoverTrigger asChild>
          <Button
            variant={anyShape ? "secondary" : "ghost"}
            size="icon"
            className="size-9"
            title="Shape tools"
            disabled={disabled}
          >
            <Square className="size-5" />
          </Button>
        </PopoverTrigger>
        <PopoverContent side="right" align="start" className="w-56 p-3">
          <div className="mb-2 text-xs font-semibold">Shapes</div>
          <div className="grid grid-cols-3 gap-1 mb-3">
            <ToolBtn active={isShape("line")} title="Line" onClick={() => setShapeTool("line")}>
              <Minus className="size-4" />
            </ToolBtn>
            <ToolBtn active={isShape("rect")} title="Rectangle" onClick={() => setShapeTool("rect")}>
              <Square className="size-4" />
            </ToolBtn>
            <ToolBtn active={isShape("circle")} title="Circle" onClick={() => setShapeTool("circle")}>
              <Circle className="size-4" />
            </ToolBtn>
            <ToolBtn active={isShape("ellipse")} title="Ellipse" onClick={() => setShapeTool("ellipse")}>
              <Circle className="size-4 scale-x-75" />
            </ToolBtn>
            <ToolBtn active={isShape("polygon")} title="Polygon" onClick={() => setShapeTool("polygon")}>
              <Pentagon className="size-4" />
            </ToolBtn>
            <ToolBtn active={isShape("star")} title="Star" onClick={() => setShapeTool("star")}>
              <Sparkles className="size-4" />
            </ToolBtn>
          </div>

          {/* Shape-specific options */}
          <div className="flex flex-col gap-2">
            {(isShape("rect") || (!anyShape)) && (
              <NumField
                label="Corner radius (mm)"
                value={shapeOpts.cornerRadiusMm}
                min={0}
                max={200}
                step={0.5}
                onChange={(v) => updateOpts({ cornerRadiusMm: v })}
              />
            )}
            {isShape("polygon") && (
              <NumField
                label="Sides"
                value={shapeOpts.sides}
                min={3}
                max={24}
                onChange={(v) => updateOpts({ sides: v })}
              />
            )}
            {isShape("star") && (
              <>
                <NumField
                  label="Points"
                  value={shapeOpts.starPoints}
                  min={3}
                  max={20}
                  onChange={(v) => updateOpts({ starPoints: v })}
                />
                <NumField
                  label="Inner radius ratio"
                  value={shapeOpts.innerRatio}
                  min={0.1}
                  max={0.9}
                  step={0.05}
                  onChange={(v) => updateOpts({ innerRatio: v })}
                />
              </>
            )}
          </div>
          <p className="mt-2 text-[10px] text-muted-foreground">
            Click + drag on canvas to draw.
          </p>
        </PopoverContent>
      </Popover>

      {/* Pen tool */}
      <ToolBtn
        active={activeTool.type === "pen"}
        title="Pen tool — click to add nodes, drag for smooth handles, click first node to close"
        onClick={() => setTool({ type: "pen" })}
      >
        <PenTool className="size-5" />
      </ToolBtn>

      {/* Text tool */}
      <ToolBtn
        active={activeTool.type === "text"}
        title="Text — click on canvas to place"
        onClick={() => setTool({ type: "text" })}
      >
        <Type className="size-5" />
      </ToolBtn>

      <input
        ref={inputRef}
        type="file"
        accept={ACCEPTED.join(",")}
        multiple
        hidden
        onChange={(e) => {
          const files = Array.from(e.target.files ?? []).filter((f) =>
            ACCEPTED.some((ext) => f.name.toLowerCase().endsWith(ext)),
          );
          if (files.length > 0) onImport(files);
          e.target.value = "";
        }}
      />
    </div>
  );
}
