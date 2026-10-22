import {
  useCallback,
  useEffect,
  useRef,
  useState,
  type CSSProperties,
} from "react";
import { Headphones, Mic, Volume2, VolumeX, RefreshCw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import {
  call,
  subscribe,
  type MixerSnapshot,
  type MixerSession,
} from "@/lib/bridge";

function Level({ peak, label }: { peak: number; label: string }) {
  return (
    <div
      role="meter"
      aria-label={label}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={Math.round(peak * 100)}
      className="h-1 rounded-full bg-muted overflow-hidden"
    >
      <div
        className="h-full bg-primary transition-[width] duration-200"
        style={{ width: `${Math.max(0, Math.min(100, peak * 100))}%` }}
      />
    </div>
  );
}

function Volume({
  value,
  muted,
  label,
  disabled,
  logarithmic,
  commit,
  mute,
}: {
  value: number;
  muted: boolean;
  label: string;
  disabled: boolean;
  logarithmic: boolean;
  commit: (volume: number) => Promise<void>;
  mute: () => Promise<void>;
}) {
  const [draft, setDraft] = useState(value);
  const [settled, setSettled] = useState(0);
  const queued = useRef<number | null>(null);
  const running = useRef(false);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const commitRef = useRef(commit);
  commitRef.current = commit;
  useEffect(() => {
    if (!running.current && queued.current === null) setDraft(value);
  }, [value, settled]);
  useEffect(
    () => () => {
      if (timer.current) clearTimeout(timer.current);
      queued.current = null;
    },
    [],
  );
  async function flush() {
    if (timer.current) clearTimeout(timer.current);
    timer.current = null;
    if (running.current) return;
    running.current = true;
    try {
      while (queued.current !== null) {
        const next = queued.current;
        queued.current = null;
        await commitRef.current(next);
      }
    } catch {
      queued.current = null;
    } finally {
      running.current = false;
      setSettled((count) => count + 1);
    }
  }
  function change(volume: number) {
    if (disabled) return;
    const next = Math.max(0, Math.min(100, volume));
    setDraft(next);
    queued.current = next;
    if (!timer.current) timer.current = setTimeout(() => void flush(), 120);
  }
  // Inverse exponential taper: preserve exact silence and full volume at the endpoints.
  const curve = 5.757;
  const position = logarithmic
    ? (Math.log1p((draft / 100) * Math.expm1(curve)) / curve) * 100
    : draft;
  return (
    <div className="flex items-center gap-3 min-w-0">
      <Button
        variant="ghost"
        size="icon"
        aria-label={`${label} ${muted ? "Ton einschalten" : "stummschalten"}`}
        disabled={disabled}
        onClick={() => void mute().catch(() => {})}
      >
        {muted ? <VolumeX size={17} /> : <Volume2 size={17} />}
      </Button>
      <input
        className="volume-slider min-w-0 flex-1"
        style={{ "--volume-position": `${position}%` } as CSSProperties}
        type="range"
        min={0}
        max={100}
        step={1}
        aria-label={`${label} Lautstärke`}
        value={position}
        disabled={disabled}
        onChange={(event) =>
          change(
            logarithmic
              ? (Math.expm1((Number(event.target.value) / 100) * curve) /
                  Math.expm1(curve)) *
                  100
              : Number(event.target.value),
          )
        }
        onPointerUp={() => void flush()}
        onKeyUp={() => void flush()}
        onBlur={() => void flush()}
        onWheel={(event) => {
          if (document.activeElement === event.currentTarget) {
            event.preventDefault();
            change(draft + (event.deltaY < 0 ? 2 : -2));
          }
        }}
      />
      <span className="text-xs tabular-nums w-10 text-right">
        {Math.round(draft)}%
      </span>
    </div>
  );
}

export function AudioMixer({
  disabled,
  visible = true,
  logarithmic = false,
  compact = false,
  changed,
}: {
  disabled: boolean;
  visible?: boolean;
  logarithmic?: boolean;
  compact?: boolean;
  changed: () => Promise<void>;
}) {
  const [snapshot, setSnapshot] = useState<MixerSnapshot | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [readError, setReadError] = useState<string | null>(null);
  const [routing, setRouting] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const alive = useRef(true),
    polling = useRef(false),
    version = useRef(0),
    readSequence = useRef(0),
    mutating = useRef(false),
    componentVisible = useRef(visible),
    hostVisible = useRef(true);
  const chain = useRef(Promise.resolve());
  useEffect(() => {
    componentVisible.current = visible;
  }, [visible]);
  const refresh = useCallback(async () => {
    const sequence = ++readSequence.current;
    const current = version.current;
    const fresh = () =>
      alive.current &&
      sequence === readSequence.current &&
      current === version.current;
    try {
      const result = await call<MixerSnapshot>("mixerState");
      if (fresh()) {
        setSnapshot(result);
        setReadError(null);
      }
    } catch (reason) {
      if (fresh()) setReadError((reason as Error).message);
      throw reason;
    }
  }, []);
  useEffect(() => {
    alive.current = true;
    const poll = async () => {
      if (
        polling.current ||
        mutating.current ||
        !componentVisible.current ||
        !hostVisible.current ||
        document.hidden
      )
        return;
      polling.current = true;
      try {
        await refresh();
      } catch {
        /* refresh keeps only current read errors */
      } finally {
        polling.current = false;
      }
    };
    void poll();
    const timer = setInterval(() => void poll(), 750);
    const stop = subscribe((message) => {
      if (message.event === "windowVisibility")
        hostVisible.current = Boolean(message.data);
    });
    return () => {
      alive.current = false;
      clearInterval(timer);
      stop();
    };
  }, [refresh]);
  function mutate(
    method: string,
    payloads: unknown[],
    route = false,
  ): Promise<void> {
    const operation = chain.current
      .catch(() => {})
      .then(async () => {
        version.current++;
        mutating.current = true;
        setError(null);
        if (route) setRouting(true);
        try {
          for (const payload of payloads) await call(method, payload);
          await refresh();
          await changed();
          if (alive.current) {
            setError(null);
            if (route)
              setNotice(
                "Ausgabezuordnung übernommen. Manche Apps wechseln erst beim nächsten Start ihrer Audiowiedergabe.",
              );
          }
        } catch (reason) {
          if (alive.current) {
            setError((reason as Error).message);
            await refresh().catch(() => {});
          }
          throw reason;
        } finally {
          version.current++;
          mutating.current = false;
          if (alive.current && route) setRouting(false);
        }
      });
    chain.current = operation;
    return operation;
  }
  const outputs = snapshot?.devices.filter((device) => !device.input) ?? [];
  const groups = new Map<string, MixerSession[]>();
  for (const session of snapshot?.sessions ?? []) {
    const key = `${session.deviceId}\n${session.appId}`;
    groups.set(key, [...(groups.get(key) ?? []), session]);
  }
  const blocked = disabled || routing;
  return (
    <Card className={compact ? "border-0 shadow-none" : "mb-6"}>
      <CardHeader>
        <div className="row gap-3">
          <CardTitle className="flex gap-2 items-center">
            <Headphones size={19} /> Audio-Mixer
          </CardTitle>
          <Button
            variant="ghost"
            size="icon"
            aria-label="Mixer aktualisieren"
            onClick={() => void refresh().catch(() => {})}
          >
            <RefreshCw size={16} />
          </Button>
        </div>
        <p className="text-xs">
          Geräte und Apps direkt regeln. App-Ausgaben folgen der
          Windows-Zuordnung.
        </p>
        <input
          aria-label="Apps im Mixer suchen"
          placeholder="App suchen …"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
        />
      </CardHeader>
      <CardContent className="space-y-4">
        {(error || readError) && (
          <div role="alert" className="notice danger select-text">
            {error || readError}
          </div>
        )}
        {notice && (
          <p role="status" className="helper">
            {notice}
          </p>
        )}
        {!snapshot && !error && <p>Mixer wird geladen …</p>}
        {snapshot?.routingWarning && (
          <p className="helper">{snapshot.routingWarning}</p>
        )}
        {snapshot?.devices
          .filter((device) => !compact || !device.input)
          .map((device) => {
            const deviceGroups = [...groups.values()].filter(
              (group) =>
                group[0].deviceId === device.id &&
                group[0].name
                  .toLocaleLowerCase()
                  .includes(search.toLocaleLowerCase()),
            );
            if (search && !deviceGroups.length) return null;
            const label = device.name;
            return (
              <section
                key={device.id}
                className="rounded-lg border border-border p-4 space-y-3"
                aria-label={label}
              >
                <div className="row gap-3 flex-wrap">
                  <strong className="text-sm flex gap-2 items-center">
                    {device.input ? <Mic size={16} /> : <Volume2 size={16} />}
                    {label}
                  </strong>
                  <div className="flex gap-2 flex-wrap">
                    <Button
                      variant={device.default ? "secondary" : "outline"}
                      size="sm"
                      disabled={blocked || device.default}
                      onClick={() =>
                        void mutate("mixerEndpoint", [
                          { deviceId: device.id, defaultRole: "standard" },
                        ]).catch(() => {})
                      }
                    >
                      {device.default ? "Standard" : "Als Standard"}
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      disabled={blocked || device.communications}
                      onClick={() =>
                        void mutate("mixerEndpoint", [
                          {
                            deviceId: device.id,
                            defaultRole: "communications",
                          },
                        ]).catch(() => {})
                      }
                    >
                      {device.communications
                        ? "Kommunikation ✓"
                        : "Kommunikation"}
                    </Button>
                  </div>
                </div>
                <Volume
                  value={device.volume}
                  muted={device.muted}
                  label={label}
                  disabled={blocked}
                  logarithmic={logarithmic}
                  commit={(volume) =>
                    mutate("mixerEndpoint", [{ deviceId: device.id, volume }])
                  }
                  mute={() =>
                    mutate("mixerEndpoint", [
                      { deviceId: device.id, muted: !device.muted },
                    ])
                  }
                />
                <Level peak={device.peak} label={`${label} Pegel`} />
                {deviceGroups.map((group) => {
                  const session = group[0],
                    appLabel = session.name;
                  const volume = Math.max(
                      ...group.map((child) => child.volume),
                    ),
                    muted = group.every((child) => child.muted);
                  const payloads = group.map((child) => ({
                    deviceId: child.deviceId,
                    sessionId: child.id,
                    processId: child.processId,
                  }));
                  const sameApp = snapshot.sessions.filter(
                    (child) => child.appId === session.appId,
                  );
                  const routeValue = sameApp.every(
                    (child) =>
                      child.routedDeviceId === sameApp[0].routedDeviceId,
                  )
                    ? (sameApp[0].routedDeviceId ?? "")
                    : "__mixed__";
                  return (
                    <div
                      key={`${device.id}/${session.appId}`}
                      className="border-t border-border pt-3 space-y-2"
                    >
                      <div className="row gap-3 flex-wrap">
                        <span className="text-sm font-medium">
                          {appLabel}
                          {group.length > 1 && (
                            <span className="helper">
                              {" "}
                              · {group.length} Sitzungen
                            </span>
                          )}
                        </span>
                        {!session.systemSounds && (
                          <select
                            className="max-w-full text-xs"
                            aria-label={`${appLabel} Ausgabe auf ${device.name}`}
                            disabled={blocked || !snapshot.routingAvailable}
                            value={routeValue}
                            onChange={(event) => {
                              const unique = [
                                ...new Map(
                                  sameApp
                                    .filter(
                                      (child) =>
                                        child.processId > 0 &&
                                        !child.systemSounds,
                                    )
                                    .map((child) => [child.processId, child]),
                                ).values(),
                              ];
                              void mutate(
                                "mixerRoute",
                                unique.map((child) => ({
                                  deviceId: child.deviceId,
                                  sessionId: child.id,
                                  processId: child.processId,
                                  targetDeviceId: event.target.value || null,
                                })),
                                true,
                              ).catch(() => {});
                            }}
                          >
                            {routeValue === "__mixed__" && (
                              <option value="__mixed__" disabled>
                                Verschiedene Ausgaben
                              </option>
                            )}
                            <option value="">Windows-Standard</option>
                            {routeValue &&
                              routeValue !== "__mixed__" &&
                              !outputs.some(
                                (output) => output.id === routeValue,
                              ) && (
                                <option value={routeValue} disabled>
                                  Gespeichertes Gerät fehlt
                                </option>
                              )}
                            {outputs.map((output) => (
                              <option key={output.id} value={output.id}>
                                {output.name}
                              </option>
                            ))}
                          </select>
                        )}
                      </div>
                      <Volume
                        value={volume}
                        muted={muted}
                        label={`${appLabel} auf ${device.name}`}
                        disabled={blocked}
                        logarithmic={logarithmic}
                        commit={(level) =>
                          mutate(
                            "mixerSession",
                            payloads.map((payload) => ({
                              ...payload,
                              volume: level,
                            })),
                          )
                        }
                        mute={() =>
                          mutate(
                            "mixerSession",
                            payloads.map((payload) => ({
                              ...payload,
                              muted: !muted,
                            })),
                          )
                        }
                      />
                      <Level
                        peak={Math.max(...group.map((child) => child.peak))}
                        label={`${appLabel} auf ${device.name} Pegel`}
                      />
                    </div>
                  );
                })}
                {!device.input && !deviceGroups.length && !search && (
                  <p className="helper">
                    Noch keine App-Audiositzung auf diesem Ausgang.
                  </p>
                )}
              </section>
            );
          })}
        {snapshot &&
          search &&
          ![...groups.values()].some((group) =>
            group[0].name
              .toLocaleLowerCase()
              .includes(search.toLocaleLowerCase()),
          ) && <p className="helper">Keine passende App gefunden.</p>}
      </CardContent>
    </Card>
  );
}
