/**
 * Floating panel for the text creation tool.
 * Appears when the user clicks on the canvas with the text tool active.
 * User types text and configures options, then clicks "Place" to convert
 * to paths and add to the project.
 */
import { useEffect, useRef, useState } from "react";
import { Check, Loader2, X } from "lucide-react";
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
import { AVAILABLE_FONTS, textToGeometry } from "@/lib/textToPath";
import type { GeometryPath } from "@/lib/project";

export interface TextPanelProps {
  onPlace: (paths: GeometryPath[], name: string) => void;
  onCancel: () => void;
}

export function TextPanel({ onPlace, onCancel }: TextPanelProps) {
  const [text, setText] = useState("Text");
  const [fontUrl, setFontUrl] = useState(AVAILABLE_FONTS[0].url);
  const [fontSizeMm, setFontSizeMm] = useState(20);
  const [letterSpacing, setLetterSpacing] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  // Focus text input on mount.
  useEffect(() => {
    inputRef.current?.select();
  }, []);

  const handlePlace = async () => {
    if (!text.trim()) return;
    setBusy(true);
    setError(null);
    try {
      const { paths } = await textToGeometry({
        text,
        fontUrl,
        fontSizeMm,
        letterSpacing,
        bold: false,
        italic: false,
      });
      if (paths.length === 0) {
        setError("No visible glyphs — try different text or font.");
        return;
      }
      const fontName =
        AVAILABLE_FONTS.find((f) => f.url === fontUrl)?.name ?? "Text";
      onPlace(paths, `${text.slice(0, 20)} (${fontName})`);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex flex-col gap-3 w-72 rounded-lg border bg-card p-4 shadow-lg">
      <div className="flex items-center justify-between">
        <span className="text-sm font-semibold">Add text</span>
        <Button variant="ghost" size="icon" className="size-6" onClick={onCancel}>
          <X className="size-3.5" />
        </Button>
      </div>

      <div className="flex flex-col gap-1">
        <Label className="text-xs">Text</Label>
        <Input
          ref={inputRef}
          value={text}
          onChange={(e) => setText(e.target.value)}
          placeholder="Enter text…"
          onKeyDown={(e) => {
            if (e.key === "Enter") void handlePlace();
            if (e.key === "Escape") onCancel();
          }}
        />
      </div>

      <div className="flex flex-col gap-1">
        <Label className="text-xs">Font</Label>
        <Select value={fontUrl} onValueChange={setFontUrl}>
          <SelectTrigger className="h-8 text-xs">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {AVAILABLE_FONTS.map((f) => (
              <SelectItem key={f.url} value={f.url}>
                {f.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <div className="grid grid-cols-2 gap-2">
        <div className="flex flex-col gap-1">
          <Label className="text-xs">Size (mm)</Label>
          <Input
            type="number"
            min={1}
            max={500}
            value={fontSizeMm}
            onChange={(e) => setFontSizeMm(Math.max(1, Number(e.target.value)))}
            className="h-8 text-xs"
          />
        </div>
        <div className="flex flex-col gap-1">
          <Label className="text-xs">Letter spacing</Label>
          <Input
            type="number"
            step={0.05}
            min={-0.5}
            max={2}
            value={letterSpacing}
            onChange={(e) => setLetterSpacing(Number(e.target.value))}
            className="h-8 text-xs"
          />
        </div>
      </div>

      {error && (
        <p className="text-xs text-destructive">{error}</p>
      )}

      <div className="flex gap-2">
        <Button
          className="flex-1 h-8 text-xs"
          onClick={() => void handlePlace()}
          disabled={busy || !text.trim()}
        >
          {busy ? <Loader2 className="size-3.5 animate-spin" /> : <Check className="size-3.5" />}
          Place
        </Button>
        <Button variant="outline" className="h-8 text-xs" onClick={onCancel}>
          Cancel
        </Button>
      </div>

      <p className="text-[10px] text-muted-foreground leading-tight">
        Text is converted to cut paths — font outlines become geometry that CAM can process.
      </p>
    </div>
  );
}
