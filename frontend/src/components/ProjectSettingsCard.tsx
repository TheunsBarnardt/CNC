import { useEffect, useState } from "react";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  displayLength,
  parseLengthToMm,
  unitSuffix,
  type ProjectDto,
  type TableOrigin,
  type TableSettings,
  type Units,
} from "@/lib/project";

interface Props {
  project: ProjectDto;
  onUpdate: (settings: { name?: string; units?: Units; table?: TableSettings }) => void;
}

const ORIGINS: { value: TableOrigin; label: string }[] = [
  { value: "BottomLeft", label: "Bottom left" },
  { value: "BottomRight", label: "Bottom right" },
  { value: "TopLeft", label: "Top left" },
  { value: "TopRight", label: "Top right" },
  { value: "Center", label: "Center" },
];

/** Table & sheet setup: name, units, table size, origin, material thickness. */
export function ProjectSettingsCard({ project, onUpdate }: Props) {
  const units = project.units;
  const suffix = unitSuffix(units);

  // Local drafts so typing doesn't fire a request per keystroke; commit on blur/Enter.
  const [name, setName] = useState(project.name);
  const [width, setWidth] = useState(displayLength(project.table.widthMm, units));
  const [height, setHeight] = useState(displayLength(project.table.heightMm, units));
  const [thickness, setThickness] = useState(
    displayLength(project.table.materialThicknessMm, units),
  );

  // Re-sync drafts when the server state changes (load/new/units switch).
  useEffect(() => {
    setName(project.name);
    setWidth(displayLength(project.table.widthMm, units));
    setHeight(displayLength(project.table.heightMm, units));
    setThickness(displayLength(project.table.materialThicknessMm, units));
  }, [project, units]);

  const commitTable = (patch: Partial<Record<"widthMm" | "heightMm" | "materialThicknessMm", string>>) => {
    const table = { ...project.table };
    const widthMm = patch.widthMm !== undefined ? parseLengthToMm(patch.widthMm, units) : table.widthMm;
    const heightMm = patch.heightMm !== undefined ? parseLengthToMm(patch.heightMm, units) : table.heightMm;
    const thicknessMm =
      patch.materialThicknessMm !== undefined
        ? parseLengthToMm(patch.materialThicknessMm, units)
        : table.materialThicknessMm;
    if (widthMm === null || heightMm === null || thicknessMm === null
        || widthMm <= 0 || heightMm <= 0 || thicknessMm < 0) {
      // Revert drafts to last good values.
      setWidth(displayLength(table.widthMm, units));
      setHeight(displayLength(table.heightMm, units));
      setThickness(displayLength(table.materialThicknessMm, units));
      return;
    }
    onUpdate({
      table: { ...table, widthMm, heightMm, materialThicknessMm: thicknessMm },
    });
  };

  return (
    <div className="grid gap-4">
      <div className="grid gap-1.5">
        <Label htmlFor="project-name">Project name</Label>
        <Input
          id="project-name"
          value={name}
          onChange={(e) => setName(e.target.value)}
          onBlur={() => name.trim() && name !== project.name && onUpdate({ name })}
          onKeyDown={(e) => e.key === "Enter" && e.currentTarget.blur()}
        />
      </div>

      <div className="grid grid-cols-2 gap-3">
        <div className="grid gap-1.5">
          <Label>Units</Label>
          <Select
            value={units}
            onValueChange={(v) => onUpdate({ units: v as Units })}
          >
            <SelectTrigger>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="Millimeters">Millimeters</SelectItem>
              <SelectItem value="Inches">Inches</SelectItem>
            </SelectContent>
          </Select>
        </div>
        <div className="grid gap-1.5">
          <Label>Origin</Label>
          <Select
            value={project.table.origin}
            onValueChange={(v) =>
              onUpdate({ table: { ...project.table, origin: v as TableOrigin } })
            }
          >
            <SelectTrigger>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {ORIGINS.map((o) => (
                <SelectItem key={o.value} value={o.value}>
                  {o.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </div>

      <div className="grid grid-cols-3 gap-3">
        <div className="grid gap-1.5">
          <Label htmlFor="table-width">Width ({suffix})</Label>
          <Input
            id="table-width"
            inputMode="decimal"
            value={width}
            onChange={(e) => setWidth(e.target.value)}
            onBlur={() => commitTable({ widthMm: width })}
            onKeyDown={(e) => e.key === "Enter" && e.currentTarget.blur()}
          />
        </div>
        <div className="grid gap-1.5">
          <Label htmlFor="table-height">Height ({suffix})</Label>
          <Input
            id="table-height"
            inputMode="decimal"
            value={height}
            onChange={(e) => setHeight(e.target.value)}
            onBlur={() => commitTable({ heightMm: height })}
            onKeyDown={(e) => e.key === "Enter" && e.currentTarget.blur()}
          />
        </div>
        <div className="grid gap-1.5">
          <Label htmlFor="material-thickness">Thickness ({suffix})</Label>
          <Input
            id="material-thickness"
            inputMode="decimal"
            value={thickness}
            onChange={(e) => setThickness(e.target.value)}
            onBlur={() => commitTable({ materialThicknessMm: thickness })}
            onKeyDown={(e) => e.key === "Enter" && e.currentTarget.blur()}
          />
        </div>
      </div>
    </div>
  );
}
