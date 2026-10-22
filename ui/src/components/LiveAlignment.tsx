import { useEffect, useRef, useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { call, type DisplaySetting, type State } from "@/lib/bridge";

function YInput({
  value,
  label,
  disabled,
  onChange,
}: {
  value: number;
  label: string;
  disabled: boolean;
  onChange: (value: number) => void;
}) {
  const [text, setText] = useState(String(value));
  useEffect(() => setText(String(value)), [value]);
  return (
    <input
      className="w-24"
      type="number"
      aria-label={label}
      min={-32768}
      max={32768}
      step={1}
      value={text}
      disabled={disabled}
      onChange={(event) => {
        setText(event.target.value);
        if (
          event.target.value !== "" &&
          Number.isFinite(event.target.valueAsNumber)
        )
          onChange(event.target.valueAsNumber);
      }}
      onBlur={() => setText(String(value))}
    />
  );
}

export function LiveAlignment({
  transaction,
  monitors,
  seconds,
  refresh,
  onKeep,
  editableIds,
  onError,
  preview,
}: {
  transaction: NonNullable<State["pending"]>;
  monitors: DisplaySetting[];
  seconds: number;
  refresh: () => Promise<void>;
  onKeep?: (monitors: DisplaySetting[]) => void;
  editableIds?: string[];
  onError?: (message: string | null) => void;
  preview: (monitors: DisplaySetting[]) => ReactNode;
}) {
  const [draft, setDraft] = useState(() =>
    structuredClone(transaction.requestedDisplays ?? monitors),
  );
  const [sending, setSending] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [closing, setClosing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const alive = useRef(true);
  const finishing = useRef(false);
  const queued = useRef<DisplaySetting[] | null>(null);
  const running = useRef<Promise<void> | null>(null);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const lastError = useRef<string | null>(null);

  useEffect(() => {
    alive.current = true;
    return () => {
      alive.current = false;
      queued.current = null;
      if (timer.current) clearTimeout(timer.current);
    };
  }, []);

  function flush(): Promise<void> {
    if (running.current) return running.current;
    const next = queued.current;
    if (!next || !alive.current) return Promise.resolve();
    queued.current = null;
    setSending(true);
    running.current = call("updateAlignment", {
      id: transaction.id,
      monitors: next,
    })
      .then(async () => {
        lastError.current = null;
        onError?.(null);
        if (alive.current) {
          setError(null);
          if (!queued.current) setDirty(false);
        }
        await refresh();
      })
      .catch((reason: Error) => {
        lastError.current = reason.message;
        onError?.(reason.message);
        if (alive.current) setError(reason.message);
        throw reason;
      })
      .finally(() => {
        running.current = null;
        if (alive.current) setSending(false);
        if (queued.current && alive.current && !finishing.current)
          scheduleFlush();
      });
    return running.current;
  }
  function scheduleFlush() {
    if (timer.current || running.current) return;
    timer.current = setTimeout(() => {
      timer.current = null;
      void flush().catch(() => {});
    }, 250);
  }
  function change(index: number, y: number) {
    if (finishing.current || !Number.isFinite(y)) return;
    const next = draft.map((monitor, i) =>
      i === index ? { ...monitor, y: Math.trunc(y) } : monitor,
    );
    setDraft(next);
    queued.current = next;
    setError(null);
    setDirty(true);
    scheduleFlush();
  }
  async function finish(keep: boolean) {
    if (finishing.current) return;
    finishing.current = true;
    setClosing(true);
    if (timer.current) {
      clearTimeout(timer.current);
      timer.current = null;
    }
    if (!keep) queued.current = null;
    try {
      await running.current?.catch(() => {});
      if (keep) {
        await flush();
        if (lastError.current) throw new Error(lastError.current);
      }
      await call(keep ? "confirm" : "rollback", keep ? transaction.id : null);
      if (keep) onKeep?.(draft);
      await refresh();
    } catch (reason) {
      onError?.((reason as Error).message);
      if (alive.current) setError((reason as Error).message);
    } finally {
      finishing.current = false;
      if (alive.current) setClosing(false);
    }
  }
  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open) void finish(false);
      }}
    >
      <DialogContent
        className="sm:max-w-3xl max-h-[calc(100vh-2rem)] overflow-y-auto"
        showCloseButton={false}
        onPointerDownOutside={(event) => event.preventDefault()}
        onInteractOutside={(event) => event.preventDefault()}
        onEscapeKeyDown={(event) => {
          event.preventDefault();
          void finish(false);
        }}
      >
        <DialogHeader>
          <DialogTitle>Monitorhöhe live abgleichen</DialogTitle>
          <DialogDescription>
            Verschiebe die anderen Monitore relativ zum Hauptmonitor. Bewege die
            Maus über die Bildschirmkante und justiere Y, bis der Übergang auf
            der passenden Höhe liegt.
          </DialogDescription>
        </DialogHeader>
        {preview(draft)}
        <div className="stack">
          {draft.map((monitor, index) => {
            const editable =
              !editableIds || editableIds.includes(monitor.deviceId);
            const neighbors = draft.filter(
              (other) =>
                other.deviceId !== monitor.deviceId &&
                (other.x + other.width === monitor.x ||
                  monitor.x + monitor.width === other.x),
            );
            const min = neighbors.length
              ? Math.max(
                  -32768,
                  Math.min(
                    ...neighbors.map((other) => other.y - monitor.height + 1),
                  ),
                )
              : -32768;
            const max = neighbors.length
              ? Math.min(
                  32768,
                  Math.max(
                    ...neighbors.map((other) => other.y + other.height - 1),
                  ),
                )
              : 32768;
            return (
              <div
                key={monitor.deviceId}
                className="rounded-lg border border-border p-3"
              >
                <div className="row gap-3">
                  <strong className="text-sm">
                    {index + 1}. {monitor.name}
                    {monitor.primary ? " · Hauptmonitor" : ""}
                    {!editable ? " · im Setup ausgeschaltet" : ""}
                  </strong>
                  <span className="text-xs text-muted-foreground">
                    Y = {monitor.y} px
                  </span>
                </div>
                <div className="flex items-center gap-3 mt-3">
                  <input
                    className="min-w-0 flex-1 accent-primary"
                    type="range"
                    aria-label={`Monitor ${index + 1} Höhe`}
                    min={min}
                    max={max}
                    step={1}
                    value={monitor.y}
                    disabled={
                      !editable || monitor.primary || closing || seconds === 0
                    }
                    onChange={(event) =>
                      change(index, Number(event.target.value))
                    }
                  />
                  <Button
                    variant="outline"
                    size="sm"
                    aria-label={`Monitor ${index + 1} höher`}
                    disabled={
                      !editable || monitor.primary || closing || seconds === 0
                    }
                    onClick={() => change(index, monitor.y - 1)}
                  >
                    −1
                  </Button>
                  <YInput
                    label={`Monitor ${index + 1} Y`}
                    value={monitor.y}
                    disabled={
                      !editable || monitor.primary || closing || seconds === 0
                    }
                    onChange={(y) => change(index, y)}
                  />
                  <Button
                    variant="outline"
                    size="sm"
                    aria-label={`Monitor ${index + 1} tiefer`}
                    disabled={
                      !editable || monitor.primary || closing || seconds === 0
                    }
                    onClick={() => change(index, monitor.y + 1)}
                  >
                    +1
                  </Button>
                </div>
              </div>
            );
          })}
        </div>
        {editableIds &&
          draft.some((monitor) => !editableIds.includes(monitor.deviceId)) && (
            <p className="helper">
              In Windows aktive Monitore, die in diesem Setup ausgeschaltet
              sind, bleiben hier unverändert. Die Monitorauswahl des Setups wird
              erst beim Aktivieren angewendet.
            </p>
          )}
        <p className="text-xs text-muted-foreground">
          Negatives Y = höher · positives Y = tiefer. Der Hauptmonitor bleibt
          bei 0. Nach jeder Änderung bleiben 90 Sekunden zum Bestätigen; ohne
          Bestätigung wird die ursprüngliche Anordnung wiederhergestellt.
        </p>
        <div role="status" className="text-sm">
          {error
            ? `Noch nicht angewendet: ${error}`
            : sending || dirty
              ? "Änderung wird angewendet …"
              : "Live-Abgleich aktiv — Mausübergang jetzt prüfen."}
        </div>
        <DialogFooter className="items-center gap-3">
          <span className="text-sm text-muted-foreground mr-auto">
            Rücksetzung in {seconds} s
          </span>
          <Button
            variant="outline"
            disabled={closing}
            onClick={() => void finish(false)}
          >
            Abbrechen & zurücksetzen
          </Button>
          <Button
            disabled={closing || seconds === 0 || !!error}
            onClick={() => void finish(true)}
          >
            Anordnung behalten
          </Button>
        </DialogFooter>
        <p className="text-xs text-muted-foreground">
          Behalten übernimmt die Anordnung in Windows. Wenn du ein Setup
          bearbeitest, werden die Höhen auch in dessen Entwurf übernommen.
          Speichere das Setup, um sie später wieder aufzurufen.
        </p>
      </DialogContent>
    </Dialog>
  );
}
