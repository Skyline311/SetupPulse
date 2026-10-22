import { useEffect, useRef } from "react";
import { ArrowUpRight, X } from "lucide-react";
import { AudioMixer } from "@/components/AudioMixer";
import { Button } from "@/components/ui/button";

export function QuickMixer({
  disabled,
  visible,
  logarithmic,
  changed,
  close,
  openFull,
}: {
  disabled: boolean;
  visible: boolean;
  logarithmic: boolean;
  changed: () => Promise<void>;
  close: () => void;
  openFull: () => void;
}) {
  const container = useRef<HTMLElement>(null);
  useEffect(() => {
    container.current?.focus();
  }, []);
  return (
    <main
      ref={container}
      tabIndex={-1}
      role="dialog"
      aria-label="Audio-Schnellmixer"
      className="h-screen flex flex-col bg-background outline-none"
      onKeyDown={(event) => {
        if (event.key === "Escape" && !event.defaultPrevented) {
          event.preventDefault();
          close();
        }
      }}
    >
      <header className="flex items-center justify-between gap-2 px-4 py-3 border-b shrink-0">
        <h1 className="text-base">Schnellmixer</h1>
        <div className="flex items-center gap-1">
          <Button size="sm" variant="ghost" onClick={openFull}>
            Voller Mixer <ArrowUpRight size={15} />
          </Button>
          <Button
            size="icon"
            variant="ghost"
            aria-label="Schnellmixer schließen"
            onClick={close}
          >
            <X size={17} />
          </Button>
        </div>
      </header>
      <div className="min-h-0 flex-1 overflow-y-auto">
        <AudioMixer
          compact
          disabled={disabled}
          visible={visible}
          logarithmic={logarithmic}
          changed={changed}
        />
      </div>
    </main>
  );
}
