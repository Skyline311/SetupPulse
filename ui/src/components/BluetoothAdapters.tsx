import { useCallback, useEffect, useRef, useState } from "react";
import { Bluetooth, RefreshCw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { call, type BluetoothAdapter } from "@/lib/bridge";

export function BluetoothAdapters() {
  const [adapters, setAdapters] = useState<BluetoothAdapter[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const alive = useRef(false),
    sequence = useRef(0);
  const refresh = useCallback(async () => {
    const current = ++sequence.current;
    setLoading(true);
    setError(null);
    try {
      const devices = await call<BluetoothAdapter[]>("bluetoothState");
      if (alive.current && sequence.current === current) setAdapters(devices);
    } catch (reason) {
      if (alive.current && sequence.current === current)
        setError((reason as Error).message);
    } finally {
      if (alive.current && sequence.current === current) setLoading(false);
    }
  }, []);
  useEffect(() => {
    alive.current = true;
    void refresh();
    return () => {
      alive.current = false;
      sequence.current++;
    };
  }, [refresh]);
  return (
    <Card>
      <CardHeader>
        <div className="row gap-3">
          <CardTitle
            role="heading"
            aria-level={2}
            className="flex gap-2 items-center"
          >
            <Bluetooth size={19} /> Bluetooth-Adapter
          </CardTitle>
          <Button
            variant="ghost"
            size="icon"
            aria-label="Bluetooth-Adapter aktualisieren"
            disabled={loading}
            onClick={() => void refresh()}
          >
            <RefreshCw size={16} />
          </Button>
        </div>
        <p className="text-xs">
          Physische Adapter und ihr Windows-Gerätestatus.
        </p>
      </CardHeader>
      <CardContent className="stack">
        {error && (
          <div role="alert" className="notice danger select-text">
            {error}
          </div>
        )}
        {loading && !adapters && <p>Adapter werden gelesen …</p>}
        {adapters?.length === 0 && (
          <p>Keine angeschlossenen physischen Bluetooth-Adapter erkannt.</p>
        )}
        {adapters?.map((adapter) => (
          <div
            key={adapter.id}
            className="device-row"
            aria-label={adapter.name}
          >
            <Bluetooth size={16} />
            <div className="min-w-0">
              <strong className="block text-sm break-words">
                {adapter.name}
              </strong>
              <span className="helper">
                {adapter.started
                  ? "Gestartet"
                  : adapter.problemCode === 22
                    ? "Deaktiviert"
                    : adapter.problemCode === null
                      ? "Status nicht verfügbar"
                      : adapter.problemCode > 0
                        ? `Windows-Fehler ${adapter.problemCode}`
                        : "Nicht gestartet"}
              </span>
            </div>
          </div>
        ))}
        <p className="helper">
          Windows unterstützt einen Bluetooth-Adapter gleichzeitig. Diese
          Ansicht liest den Status; Adapter-Umschaltung ist noch nicht
          verfügbar.
        </p>
      </CardContent>
    </Card>
  );
}
