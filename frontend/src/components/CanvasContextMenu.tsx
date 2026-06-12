import { useEffect, useRef } from "react";

export interface ContextMenuItem {
  label: string;
  onClick: () => void;
  divider?: boolean;
  disabled?: boolean;
  danger?: boolean;
}

interface Props {
  x: number;
  y: number;
  items: ContextMenuItem[];
  onClose: () => void;
  /** Container dimensions so the menu doesn't overflow. */
  containerW?: number;
  containerH?: number;
}

export function CanvasContextMenu({ x, y, items, onClose, containerW = 9999 }: Props) {
  const menuRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) onClose();
    };
    const keyHandler = (e: KeyboardEvent) => { if (e.key === "Escape") onClose(); };
    // Small delay so the right-click that opened the menu doesn't immediately close it.
    const id = setTimeout(() => {
      window.addEventListener("mousedown", handler);
      window.addEventListener("keydown", keyHandler);
    }, 50);
    return () => {
      clearTimeout(id);
      window.removeEventListener("mousedown", handler);
      window.removeEventListener("keydown", keyHandler);
    };
  }, [onClose]);

  // Clamp so the menu stays inside the container.
  const MENU_W = 192;
  const left = Math.min(x, containerW - MENU_W - 4);
  const top = y; // vertical clamping handled by maxHeight

  return (
    <div
      ref={menuRef}
      className="absolute z-50 min-w-48 rounded-md border bg-popover py-1 shadow-lg text-sm text-popover-foreground"
      style={{ left, top }}
      onContextMenu={(e) => e.preventDefault()}
    >
      {items.map((item, i) => (
        <div key={i}>
          {item.divider && i > 0 && <div className="my-1 border-t" />}
          <button
            className={[
              "w-full px-3 py-1.5 text-left transition-colors",
              item.disabled
                ? "cursor-default opacity-40"
                : item.danger
                  ? "hover:bg-destructive/10 text-destructive"
                  : "hover:bg-accent hover:text-accent-foreground",
            ].join(" ")}
            disabled={item.disabled}
            onMouseDown={(e) => {
              e.preventDefault();
              if (!item.disabled) {
                item.onClick();
                onClose();
              }
            }}
          >
            {item.label}
          </button>
        </div>
      ))}
    </div>
  );
}
