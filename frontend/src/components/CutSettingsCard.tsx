import { useCallback, useEffect, useState } from "react";
import { Plus, Save, Trash2 } from "lucide-react";
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
import {
  projectApi,
  type CamSettings,
  type LeadType,
  type MachineType,
  type MaterialProfile,
} from "@/lib/project";

const LEAD_TYPES: LeadType[] = ["None", "Line", "Arc"];
const MACHINE_TYPES: { value: MachineType; label: string }[] = [
  { value: "Plasma", label: "Plasma" },
  { value: "Laser", label: "Laser" },
];

interface Props {
  /** Sheet thickness from table settings — used when saving a new profile. */
  materialThicknessMm: number;
}

/**
 * Plasma cut settings + material profiles. Selecting a profile copies its
 * numbers into the project CAM settings; the lead fields stay per-project
 * (lead strategy is geometry preference, not a material property).
 */
export function CutSettingsCard({ materialThicknessMm }: Props) {
  const [cam, setCam] = useState<CamSettings | null>(null);
  const [profiles, setProfiles] = useState<MaterialProfile[]>([]);
  const [profileId, setProfileId] = useState<string>("");
  const [newName, setNewName] = useState("");
  const [error, setError] = useState<string | null>(null);
  // Draft strings for numeric fields so typing doesn't fire requests.
  const [draft, setDraft] = useState<Record<string, string>>({});

  useEffect(() => {
    Promise.all([projectApi.getCam(), projectApi.listProfiles()])
      .then(([c, p]) => {
        setCam(c);
        setProfiles(p);
      })
      .catch((err: Error) => setError(err.message));
  }, []);

  const commitCam = useCallback((next: CamSettings) => {
    setCam(next);
    projectApi
      .updateCam(next)
      .then(setCam)
      .catch((err: Error) => setError(err.message));
  }, []);

  /** Numeric field helper: draft while typing, validate + PUT on blur/Enter. */
  const numField = (
    key: keyof CamSettings & string,
    label: string,
    opts: { min?: number } = {},
  ) => {
    if (!cam) return null;
    const value = draft[key] ?? String(cam[key]);
    const commit = () => {
      const n = Number(value);
      const bad = !Number.isFinite(n) || n < (opts.min ?? 0);
      setDraft((d) => {
        const { [key]: _, ...rest } = d;
        return rest;
      });
      if (bad || n === cam[key]) return;
      commitCam({ ...cam, [key]: n });
    };
    return (
      <div className="grid gap-1">
        <Label className="text-xs" htmlFor={`cam-${key}`}>{label}</Label>
        <Input
          id={`cam-${key}`}
          className="h-8 text-xs"
          inputMode="decimal"
          value={value}
          onChange={(e) => setDraft((d) => ({ ...d, [key]: e.target.value }))}
          onBlur={commit}
          onKeyDown={(e) => e.key === "Enter" && e.currentTarget.blur()}
        />
      </div>
    );
  };

  const applyProfile = (id: string) => {
    setProfileId(id);
    const p = profiles.find((x) => x.id === id);
    if (!p || !cam) return;
    commitCam({
      ...cam,
      kerfWidthMm: p.kerfWidthMm,
      feedRateMmMin: p.feedRateMmMin,
      pierceDelayS: p.pierceDelayS,
      cutHeightMm: p.cutHeightMm,
      pierceHeightMm: p.pierceHeightMm,
    });
  };

  const saveNewProfile = async () => {
    if (!cam || !newName.trim()) return;
    try {
      const created = await projectApi.createProfile({
        name: newName.trim(),
        material: "",
        thicknessMm: materialThicknessMm,
        kerfWidthMm: cam.kerfWidthMm,
        feedRateMmMin: cam.feedRateMmMin,
        pierceDelayS: cam.pierceDelayS,
        cutHeightMm: cam.cutHeightMm,
        pierceHeightMm: cam.pierceHeightMm,
      });
      setProfiles((p) => [...p, created]);
      setProfileId(created.id);
      setNewName("");
      setError(null);
    } catch (err) {
      setError((err as Error).message);
    }
  };

  const updateSelectedProfile = async () => {
    const p = profiles.find((x) => x.id === profileId);
    if (!p || !cam) return;
    try {
      const updated = await projectApi.updateProfile({
        ...p,
        kerfWidthMm: cam.kerfWidthMm,
        feedRateMmMin: cam.feedRateMmMin,
        pierceDelayS: cam.pierceDelayS,
        cutHeightMm: cam.cutHeightMm,
        pierceHeightMm: cam.pierceHeightMm,
      });
      setProfiles((list) => list.map((x) => (x.id === updated.id ? updated : x)));
      setError(null);
    } catch (err) {
      setError((err as Error).message);
    }
  };

  const deleteSelectedProfile = async () => {
    if (!profileId) return;
    try {
      await projectApi.deleteProfile(profileId);
      setProfiles((list) => list.filter((x) => x.id !== profileId));
      setProfileId("");
      setError(null);
    } catch (err) {
      setError((err as Error).message);
    }
  };

  if (!cam) {
    return <p className="text-xs text-muted-foreground">{error ?? "Loading…"}</p>;
  }

  const isLaser = (cam.operationMode ?? "Plasma") === "Laser";

  const leadSelect = (
    typeKey: "leadInType" | "leadOutType",
    label: string,
  ) => (
    <div className="grid gap-1">
      <Label className="text-xs">{label}</Label>
      <Select
        value={cam[typeKey]}
        onValueChange={(v) => commitCam({ ...cam, [typeKey]: v as LeadType })}
      >
        <SelectTrigger className="h-8 text-xs">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {LEAD_TYPES.map((t) => (
            <SelectItem key={t} value={t}>
              {t}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  );

  return (
    <div className="grid gap-3">
      {/* Machine type selector */}
      <div className="grid gap-1">
        <Label className="text-xs">Machine type</Label>
        <Select
          value={cam.operationMode ?? "Plasma"}
          onValueChange={(v) => commitCam({ ...cam, operationMode: v as MachineType })}
        >
          <SelectTrigger className="h-8 text-xs">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {MACHINE_TYPES.map((m) => (
              <SelectItem key={m.value} value={m.value}>
                {m.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      {/* Shared: feed rate */}
      <div className="grid grid-cols-2 gap-2">
        {numField("feedRateMmMin", "Feed (mm/min)", { min: 1 })}
        {/* Laser: power % */}
        {isLaser && numField("laserPowerPercent", "Power (%)", { min: 0 })}
      </div>

      {isLaser && (
        <p className="text-xs text-muted-foreground">
          Set <code className="font-mono">$32=1</code> on the controller to enable GRBL laser mode.
        </p>
      )}

      {/* Plasma-only settings */}
      {!isLaser && (
        <>
          {/* Material profile picker */}
          <div className="grid gap-1">
            <Label className="text-xs">Material profile</Label>
            <div className="flex items-center gap-1.5">
              <Select value={profileId} onValueChange={applyProfile}>
                <SelectTrigger className="h-8 min-w-0 flex-1 text-xs">
                  <SelectValue placeholder="Pick a profile…" />
                </SelectTrigger>
                <SelectContent>
                  {profiles.map((p) => (
                    <SelectItem key={p.id} value={p.id}>
                      {p.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <Button
                variant="ghost"
                size="icon"
                className="size-8 shrink-0"
                title="Update profile with current settings"
                disabled={!profileId}
                onClick={() => void updateSelectedProfile()}
              >
                <Save className="size-4" />
              </Button>
              <Button
                variant="ghost"
                size="icon"
                className="size-8 shrink-0 text-destructive"
                title="Delete profile"
                disabled={!profileId}
                onClick={() => void deleteSelectedProfile()}
              >
                <Trash2 className="size-4" />
              </Button>
            </div>
            <div className="flex items-center gap-1.5">
              <Input
                className="h-8 min-w-0 flex-1 text-xs"
                placeholder="Save current as… (name)"
                value={newName}
                onChange={(e) => setNewName(e.target.value)}
                onKeyDown={(e) => e.key === "Enter" && void saveNewProfile()}
              />
              <Button
                variant="outline"
                size="icon"
                className="size-8 shrink-0"
                title="Save current settings as a new profile"
                disabled={!newName.trim()}
                onClick={() => void saveNewProfile()}
              >
                <Plus className="size-4" />
              </Button>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-2">
            {numField("kerfWidthMm", "Kerf (mm)")}
            {numField("pierceDelayS", "Pierce delay (s)")}
            {numField("cutHeightMm", "Cut height (mm)")}
            {numField("pierceHeightMm", "Pierce height (mm)")}
          </div>

          <div className="grid grid-cols-2 gap-2">
            {leadSelect("leadInType", "Lead-in")}
            {numField("leadInLengthMm", "Lead-in (mm)")}
            {leadSelect("leadOutType", "Lead-out")}
            {numField("leadOutLengthMm", "Lead-out (mm)")}
          </div>
        </>
      )}

      {error && (
        <div className="rounded-md border border-destructive/50 bg-destructive/10 px-3 py-1.5 text-xs text-destructive">
          {error}
        </div>
      )}
    </div>
  );
}
