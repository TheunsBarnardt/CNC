import { useEffect, useState } from "react";
import { Download, FileCode2, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
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
  type GcodeResult,
  type PostProcessorInfo,
} from "@/lib/project";

/**
 * Generate G-code from the current project via a chosen post-processor,
 * preview it, and download it as a .nc file. Generation is always an explicit
 * user action — nothing here talks to a machine.
 */
export function GcodePanel({ hasParts }: { hasParts: boolean }) {
  const [posts, setPosts] = useState<PostProcessorInfo[]>([]);
  const [postId, setPostId] = useState<string | null>(null);
  const [result, setResult] = useState<GcodeResult | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    projectApi
      .listPosts()
      .then((list) => {
        setPosts(list);
        setPostId(list.find((p) => p.isDefault)?.id ?? list[0]?.id ?? null);
      })
      .catch((err: Error) => setError(err.message));
  }, []);

  const generate = async () => {
    setBusy(true);
    setError(null);
    try {
      setResult(await projectApi.generateGcode(postId ?? undefined));
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const download = () => {
    if (!result) return;
    const blob = new Blob([result.gcode], { type: "text/plain" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = result.fileName;
    a.click();
    URL.revokeObjectURL(url);
  };

  const selectedPost = posts.find((p) => p.id === postId);

  return (
    <div className="space-y-3">
      {posts.length > 1 ? (
        <div className="space-y-1.5">
          <Label className="text-xs">Post-processor</Label>
          <Select value={postId ?? undefined} onValueChange={setPostId}>
            <SelectTrigger className="h-8 text-xs">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {posts.map((p) => (
                <SelectItem key={p.id} value={p.id}>
                  {p.displayName}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      ) : (
        selectedPost && (
          <p className="text-xs text-muted-foreground">
            {selectedPost.displayName} — {selectedPost.description}
          </p>
        )
      )}

      <div className="flex gap-2">
        <Button
          size="sm"
          className="flex-1"
          onClick={() => void generate()}
          disabled={busy || !hasParts || !postId}
        >
          {busy ? (
            <Loader2 className="size-4 animate-spin" />
          ) : (
            <FileCode2 className="size-4" />
          )}
          Generate G-code
        </Button>
        <Button
          size="sm"
          variant="outline"
          onClick={download}
          disabled={!result}
          title={result ? `Download ${result.fileName}` : "Generate first"}
        >
          <Download className="size-4" />
        </Button>
      </div>

      {!hasParts && (
        <p className="text-xs text-muted-foreground">
          Place at least one part on the table first.
        </p>
      )}

      {error && (
        <div className="rounded-md border border-destructive/50 bg-destructive/10 px-3 py-1.5 text-xs text-destructive">
          {error}
        </div>
      )}

      {result && (
        <>
          <p className="text-xs text-muted-foreground">
            {result.cutCount} cut{result.cutCount === 1 ? "" : "s"} ·{" "}
            {result.lineCount} lines · cut {result.totalCutLengthMm.toFixed(0)}
            mm · rapids {result.totalRapidLengthMm.toFixed(0)}mm
          </p>
          {result.warnings.map((w) => (
            <div
              key={w}
              className="rounded-md border border-yellow-600/50 bg-yellow-500/10 px-3 py-1.5 text-xs text-yellow-700 dark:text-yellow-400"
            >
              {w}
            </div>
          ))}
          <pre className="max-h-64 overflow-auto rounded-md border bg-muted/40 p-2 font-mono text-[11px] leading-snug">
            {result.gcode}
          </pre>
        </>
      )}
    </div>
  );
}
