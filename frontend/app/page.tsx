"use client";

import { useCallback, useEffect, useMemo, useState } from "react";

type ArchiveType = "mailbox" | "journal" | "fsa";
type MappingStatus = "mapped" | "pending_mapping";

interface ArchiveResult {
  source_archive_id: string;
  archive_type: ArchiveType;
  owner_upn: string | null;
  target_archive_id: string | null;
  status: MappingStatus;
  reason: string | null;
  legal_hold: boolean;
  item_count: number;
  total_bytes: number;
}

interface DiscoveryReport {
  archive_count: number;
  item_count: number;
  mapped_archive_count: number;
  pending_archive_count: number;
  eligible_item_count: number;
  pending_item_count: number;
  eligible_bytes: number;
  legal_hold_archive_count: number;
  archives: ArchiveResult[];
}

interface MigrationFailure {
  item_id: string;
  category: string;
  error: string;
  http_status_code: number | null;
}

interface MigrationReport {
  run_id: string;
  started_at_utc: string;
  completed_at_utc: string;
  worker_count: number;
  dry_run: boolean;
  scanned_item_count: number;
  filtered_out_item_count: number;
  eligible_item_count: number;
  pending_mapping_item_count: number;
  checkpoint_skipped_item_count: number;
  attempted_item_count: number;
  uploaded_item_count: number;
  existing_item_count: number;
  failed_item_count: number;
  retry_count: number;
  migrated_bytes: number;
  planned_bytes: number;
  physical_sis_reads: number;
  cached_sis_parts: number;
  failures: MigrationFailure[];
}

interface ReconciliationReport {
  run_id: string;
  completed_at_utc: string;
  is_reconciled: boolean;
  expected_item_count: number;
  target_item_count: number;
  matched_item_count: number;
  source_logical_bytes: number;
  target_logical_bytes: number;
  missing_item_ids: string[];
  unexpected_item_ids: string[];
  mismatches: Array<{ source_item_id: string; fields: string[] }>;
}

interface TargetState {
  item_count: number;
  unique_part_count: number;
  logical_bytes: number;
  physical_bytes: number;
}

type Operation = "loading" | "dry-run" | "migrate" | "reconcile" | "reset" | null;

const API_BASE = process.env.NEXT_PUBLIC_API_BASE_URL ?? "";

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    ...init,
    headers: { "Content-Type": "application/json", ...init?.headers },
  });

  if (!response.ok) {
    const body = await response.json().catch(() => null) as { message?: string } | null;
    throw new Error(body?.message ?? `İstek başarısız oldu (${response.status}).`);
  }

  return response.json() as Promise<T>;
}

function bytes(value: number): string {
  if (value < 1024) return `${value} B`;
  return `${(value / 1024).toFixed(1)} KB`;
}

function duration(start: string, end: string): string {
  const milliseconds = Math.max(0, new Date(end).getTime() - new Date(start).getTime());
  return milliseconds < 1000 ? `${milliseconds} ms` : `${(milliseconds / 1000).toFixed(1)} s`;
}

function archiveLabel(type: ArchiveType): string {
  return type === "fsa" ? "FSA" : type.charAt(0).toUpperCase() + type.slice(1);
}

function Icon({ name, className = "h-5 w-5" }: { name: "archive" | "engine" | "target" | "shield" | "refresh" | "check" | "play" | "search"; className?: string }) {
  const paths = {
    archive: <><path d="M4 7h16v13H4z"/><path d="M3 4h18v3H3z"/><path d="M9 11h6"/></>,
    engine: <><path d="M12 3v3M12 18v3M3 12h3M18 12h3"/><circle cx="12" cy="12" r="5"/><circle cx="12" cy="12" r="1.5"/></>,
    target: <><circle cx="12" cy="12" r="8"/><circle cx="12" cy="12" r="4"/><path d="m14 10 6-6M17 4h3v3"/></>,
    shield: <path d="M12 3 5 6v5c0 4.6 2.8 8 7 10 4.2-2 7-5.4 7-10V6l-7-3Z"/>,
    refresh: <><path d="M20 11a8 8 0 1 0-2.3 5.7"/><path d="M20 5v6h-6"/></>,
    check: <path d="m5 12 4 4L19 6"/>,
    play: <path d="m8 5 11 7-11 7V5Z"/>,
    search: <><circle cx="11" cy="11" r="7"/><path d="m16 16 5 5"/></>,
  };
  return <svg aria-hidden="true" className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">{paths[name]}</svg>;
}

export default function Home() {
  const [discovery, setDiscovery] = useState<DiscoveryReport | null>(null);
  const [target, setTarget] = useState<TargetState | null>(null);
  const [migration, setMigration] = useState<MigrationReport | null>(null);
  const [reconciliation, setReconciliation] = useState<ReconciliationReport | null>(null);
  const [operation, setOperation] = useState<Operation>("loading");
  const [error, setError] = useState<string | null>(null);
  const [workers, setWorkers] = useState(4);
  const [useCheckpoint, setUseCheckpoint] = useState(true);
  const [archiveId, setArchiveId] = useState("");
  const [folder, setFolder] = useState("");
  const [showFilters, setShowFilters] = useState(false);

  const loadSnapshot = useCallback(async () => {
    const discoveryReport = await api<DiscoveryReport>("/api/discovery");
    setDiscovery(discoveryReport);
    try {
      setTarget(await api<TargetState>("/api/target-state"));
    } catch {
      setTarget(null);
    }
  }, []);

  useEffect(() => {
    const timeoutId = window.setTimeout(() => {
      void loadSnapshot()
        .catch((reason: unknown) => setError(reason instanceof Error ? reason.message : "Demo yüklenemedi."))
        .finally(() => setOperation(null));
    }, 0);

    return () => window.clearTimeout(timeoutId);
  }, [loadSnapshot]);

  const requestBody = useMemo(() => JSON.stringify({
    workers,
    use_checkpoint: useCheckpoint,
    archive_id: archiveId || null,
    folder: folder || null,
  }), [archiveId, folder, useCheckpoint, workers]);

  async function runMigration(dryRun: boolean) {
    setOperation(dryRun ? "dry-run" : "migrate");
    setError(null);
    setReconciliation(null);
    try {
      const report = await api<MigrationReport>(dryRun ? "/api/dry-run" : "/api/migrate", {
        method: "POST",
        body: requestBody,
      });
      setMigration(report);
      if (!dryRun) setTarget(await api<TargetState>("/api/target-state"));
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Migration başlatılamadı.");
    } finally {
      setOperation(null);
    }
  }

  async function reconcile() {
    setOperation("reconcile");
    setError(null);
    try {
      setReconciliation(await api<ReconciliationReport>("/api/reconcile", { method: "POST", body: "{}" }));
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Reconciliation tamamlanamadı.");
    } finally {
      setOperation(null);
    }
  }

  async function resetDemo() {
    setOperation("reset");
    setError(null);
    try {
      await api<{ status: string }>("/api/reset", { method: "POST", body: "{}" });
      setMigration(null);
      setReconciliation(null);
      setTarget({ item_count: 0, unique_part_count: 0, logical_bytes: 0, physical_bytes: 0 });
      await loadSnapshot();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Demo sıfırlanamadı.");
    } finally {
      setOperation(null);
    }
  }

  const busy = operation !== null;
  const physicalRatio = target?.logical_bytes
    ? Math.round((target.physical_bytes / target.logical_bytes) * 100)
    : 0;
  const mappedPercent = discovery?.archive_count
    ? Math.round((discovery.mapped_archive_count / discovery.archive_count) * 100)
    : 0;

  return (
    <main className="relative min-h-screen overflow-hidden pb-16">
      <div className="grid-fade pointer-events-none absolute inset-0" />
      <div className="relative mx-auto max-w-[1440px] px-4 sm:px-6 lg:px-10">
        <header className="flex min-h-20 items-center justify-between border-b border-white/8 py-4">
          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-xl border border-cyan-300/25 bg-cyan-300/10 text-cyan-300 shadow-[0_0_28px_rgba(34,211,238,0.12)]">
              <Icon name="engine" />
            </div>
            <div>
              <div className="flex items-baseline gap-2">
                <span className="text-lg font-semibold tracking-tight text-white">storionX</span>
                <span className="text-[10px] font-semibold uppercase tracking-[0.22em] text-cyan-300/80">Migration Console</span>
              </div>
              <p className="text-xs text-slate-500">Enterprise Vault migration demonstration</p>
            </div>
          </div>
          <div className="flex items-center gap-3">
            <div className="hidden items-center gap-2 rounded-full border border-emerald-400/15 bg-emerald-400/7 px-3 py-1.5 text-xs text-emerald-300 sm:flex">
              <span className={`h-1.5 w-1.5 rounded-full bg-emerald-400 ${busy ? "working-dot" : ""}`} />
              {busy ? "İşlem sürüyor" : "Sistem hazır"}
            </div>
            <button onClick={resetDemo} disabled={busy} className="inline-flex items-center gap-2 rounded-lg border border-white/10 bg-white/[0.035] px-3.5 py-2 text-xs font-medium text-slate-300 transition hover:border-white/20 hover:bg-white/[0.07] disabled:cursor-not-allowed disabled:opacity-45">
              <Icon name="refresh" className={`h-4 w-4 ${operation === "reset" ? "animate-spin" : ""}`} />
              Demo&apos;yu sıfırla
            </button>
          </div>
        </header>

        <section className="grid gap-8 pb-8 pt-10 lg:grid-cols-[1.25fr_.75fr] lg:items-end">
          <div>
            <div className="mb-4 inline-flex items-center gap-2 rounded-full border border-cyan-300/15 bg-cyan-300/[0.06] px-3 py-1 text-[11px] font-medium uppercase tracking-[0.16em] text-cyan-200">
              <span className="h-1 w-1 rounded-full bg-cyan-300" />
              Live migration workspace
            </div>
            <h1 className="max-w-3xl text-3xl font-semibold leading-tight tracking-[-0.03em] text-white sm:text-4xl lg:text-[46px]">
              Arşiv verisini güvenle taşı,
              <span className="block text-slate-400">her adımı doğrula.</span>
            </h1>
            <p className="mt-4 max-w-2xl text-sm leading-6 text-slate-400 sm:text-base">
              Mailbox, journal ve FSA arşivlerini keşfedin; SIS içeriklerini doğrulayın ve idempotent ingestion akışını uçtan uca izleyin.
            </p>
          </div>
          <div className="grid grid-cols-3 gap-2 sm:gap-3">
            <Metric label="Kaynak item" value={discovery?.item_count ?? "—"} />
            <Metric label="Taşınabilir" value={discovery?.eligible_item_count ?? "—"} tone="cyan" />
            <Metric label="Hedef item" value={target?.item_count ?? "—"} tone="green" />
          </div>
        </section>

        {error && (
          <div className="mb-6 flex items-start justify-between gap-4 rounded-xl border border-rose-400/20 bg-rose-400/[0.07] px-4 py-3 text-sm text-rose-200">
            <span>{error}</span>
            <button aria-label="Hatayı kapat" onClick={() => setError(null)} className="text-rose-300/70 hover:text-rose-200">×</button>
          </div>
        )}

        <section className="panel rounded-2xl p-4 sm:p-6">
          <div className="mb-5 flex flex-wrap items-center justify-between gap-3">
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">Migration topology</p>
              <h2 className="mt-1 text-base font-semibold text-slate-100">Kaynak → Pipeline → Hedef</h2>
            </div>
            <div className="flex items-center gap-2 text-[11px] text-slate-500">
              <span>Mapping %{mappedPercent}</span>
              <span className="h-3 w-px bg-white/10" />
              <span>SHA-256 verified</span>
            </div>
          </div>
          <div className="grid items-center gap-3 md:grid-cols-[1fr_72px_1.2fr_72px_1fr]">
            <TopologyNode icon="archive" eyebrow="Source" title="Enterprise Vault" detail={`${discovery?.archive_count ?? 0} archive · ${discovery?.item_count ?? 0} item`} />
            <div className="flow-line hidden md:block" />
            <TopologyNode icon="engine" eyebrow="Processing" title="Migration Engine" detail="Rehydrate · transform · retry" active={busy} />
            <div className="flow-line hidden md:block" />
            <TopologyNode icon="target" eyebrow="Destination" title="storionX" detail={`${target?.item_count ?? 0} item · ${target?.unique_part_count ?? 0} unique part`} />
          </div>
        </section>

        <section className="mt-6 grid gap-6 xl:grid-cols-[1.35fr_.65fr]">
          <div className="panel overflow-hidden rounded-2xl">
            <div className="flex items-center justify-between border-b border-white/8 px-5 py-4 sm:px-6">
              <div>
                <div className="flex items-center gap-2">
                  <span className="flex h-6 w-6 items-center justify-center rounded-md bg-cyan-300/10 text-xs font-semibold text-cyan-300">1</span>
                  <h2 className="font-semibold text-slate-100">Archive discovery</h2>
                </div>
                <p className="mt-1 pl-8 text-xs text-slate-500">Kaynak arşivlerin hedef eşlemeleri ve koruma politikaları</p>
              </div>
              <span className="rounded-full border border-white/10 px-2.5 py-1 text-[11px] text-slate-400">{discovery?.mapped_archive_count ?? 0}/{discovery?.archive_count ?? 0} mapped</span>
            </div>
            <div className="overflow-x-auto">
              <table className="w-full min-w-[720px] text-left text-sm">
                <thead className="border-b border-white/6 bg-black/10 text-[10px] uppercase tracking-[0.14em] text-slate-500">
                  <tr>
                    <th className="px-6 py-3 font-medium">Archive</th>
                    <th className="px-4 py-3 font-medium">Owner / source</th>
                    <th className="px-4 py-3 font-medium">Target</th>
                    <th className="px-4 py-3 text-right font-medium">Items</th>
                    <th className="px-4 py-3 font-medium">Policy</th>
                    <th className="px-6 py-3 font-medium">Status</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-white/6">
                  {discovery?.archives.map((archive) => (
                    <tr key={archive.source_archive_id} className="transition hover:bg-white/[0.025]">
                      <td className="px-6 py-4">
                        <div className="flex items-center gap-3">
                          <span className="flex h-8 w-8 items-center justify-center rounded-lg border border-white/8 bg-white/[0.035] font-mono text-xs text-cyan-200">{archive.source_archive_id}</span>
                          <span className="text-xs font-medium text-slate-300">{archiveLabel(archive.archive_type)}</span>
                        </div>
                      </td>
                      <td className="max-w-[210px] truncate px-4 py-4 text-xs text-slate-400">{archive.owner_upn ?? (archive.archive_type === "fsa" ? "Finance file share" : "Journal stream")}</td>
                      <td className="px-4 py-4 font-mono text-[11px] text-slate-300">{archive.target_archive_id ?? "—"}</td>
                      <td className="px-4 py-4 text-right font-mono text-xs text-slate-300">{archive.item_count}</td>
                      <td className="px-4 py-4">
                        {archive.legal_hold ? <Badge tone="amber">Legal hold</Badge> : <span className="text-xs text-slate-600">Standard</span>}
                      </td>
                      <td className="px-6 py-4">
                        {archive.status === "mapped" ? <Badge tone="green">Mapped</Badge> : <Badge tone="amber">Pending</Badge>}
                      </td>
                    </tr>
                  ))}
                  {!discovery && <tr><td colSpan={6} className="px-6 py-10 text-center text-sm text-slate-500">Discovery yükleniyor…</td></tr>}
                </tbody>
              </table>
            </div>
          </div>

          <div className="panel rounded-2xl p-5 sm:p-6">
            <div className="flex items-center gap-2">
              <span className="flex h-6 w-6 items-center justify-center rounded-md bg-cyan-300/10 text-xs font-semibold text-cyan-300">2</span>
              <h2 className="font-semibold text-slate-100">Migration control</h2>
            </div>
            <p className="mt-2 text-xs leading-5 text-slate-500">İşçi sayısını ve checkpoint davranışını seçerek migration’ı başlatın.</p>

            <div className="mt-6">
              <div className="mb-2 flex items-center justify-between text-xs">
                <label htmlFor="workers" className="font-medium text-slate-300">Parallel workers</label>
                <span className="font-mono text-cyan-300">{workers}</span>
              </div>
              <input id="workers" type="range" min="1" max="8" value={workers} onChange={(event) => setWorkers(Number(event.target.value))} className="h-1.5 w-full cursor-pointer accent-cyan-300" />
              <div className="mt-1.5 flex justify-between font-mono text-[9px] text-slate-600"><span>1</span><span>8</span></div>
            </div>

            <label className="mt-5 flex cursor-pointer items-center justify-between rounded-xl border border-white/8 bg-black/10 px-3.5 py-3">
              <div>
                <span className="block text-xs font-medium text-slate-300">Checkpoint kullan</span>
                <span className="mt-0.5 block text-[10px] text-slate-600">Tamamlanan item’ları sonraki çalışmada atla</span>
              </div>
              <input type="checkbox" checked={useCheckpoint} onChange={(event) => setUseCheckpoint(event.target.checked)} className="h-4 w-4 accent-cyan-300" />
            </label>

            <button onClick={() => setShowFilters((value) => !value)} className="mt-3 flex w-full items-center justify-between px-1 py-2 text-xs text-slate-500 hover:text-slate-300">
              <span className="inline-flex items-center gap-2"><Icon name="search" className="h-3.5 w-3.5" /> İsteğe bağlı filtreler</span>
              <span>{showFilters ? "−" : "+"}</span>
            </button>

            {showFilters && (
              <div className="grid gap-3 border-t border-white/8 pt-3 sm:grid-cols-2 xl:grid-cols-1 2xl:grid-cols-2">
                <Field label="Archive ID" value={archiveId} onChange={setArchiveId} placeholder="A1" />
                <Field label="Folder" value={folder} onChange={setFolder} placeholder="Inbox" />
              </div>
            )}

            <div className="mt-5 grid grid-cols-2 gap-2.5">
              <button disabled={busy || !discovery} onClick={() => runMigration(true)} className="rounded-xl border border-white/10 bg-white/[0.035] px-3 py-3 text-xs font-semibold text-slate-300 transition hover:bg-white/[0.07] disabled:cursor-not-allowed disabled:opacity-40">
                {operation === "dry-run" ? "Planlanıyor…" : "Dry run"}
              </button>
              <button disabled={busy || !discovery} onClick={() => runMigration(false)} className="inline-flex items-center justify-center gap-2 rounded-xl bg-cyan-300 px-3 py-3 text-xs font-bold text-[#062029] shadow-[0_8px_25px_rgba(34,211,238,0.18)] transition hover:bg-cyan-200 disabled:cursor-not-allowed disabled:opacity-40">
                <Icon name="play" className="h-3.5 w-3.5" />
                {operation === "migrate" ? "Taşınıyor…" : "Migration başlat"}
              </button>
            </div>
          </div>
        </section>

        <section className="mt-6 grid gap-6 xl:grid-cols-[1fr_1fr]">
          <div className="panel rounded-2xl p-5 sm:p-6">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <div className="flex items-center gap-2">
                  <span className="flex h-6 w-6 items-center justify-center rounded-md bg-cyan-300/10 text-xs font-semibold text-cyan-300">3</span>
                  <h2 className="font-semibold text-slate-100">Migration result</h2>
                </div>
                <p className="mt-1 pl-8 text-xs text-slate-500">Checkpoint, retry ve ingestion özeti</p>
              </div>
              {migration && <Badge tone={migration.failed_item_count ? "red" : migration.dry_run ? "blue" : "green"}>{migration.dry_run ? "Dry run" : migration.failed_item_count ? "Completed with errors" : "Completed"}</Badge>}
            </div>

            {migration ? (
              <>
                <div className="mt-6 grid grid-cols-2 gap-3 sm:grid-cols-4">
                  <ResultMetric label="Attempted" value={migration.attempted_item_count} />
                  <ResultMetric label="Uploaded" value={migration.uploaded_item_count} tone="green" />
                  <ResultMetric label="Existing" value={migration.existing_item_count} tone="cyan" />
                  <ResultMetric label="CP skipped" value={migration.checkpoint_skipped_item_count} tone="amber" />
                </div>
                <div className="mt-5 grid gap-3 border-t border-white/8 pt-5 sm:grid-cols-3">
                  <InlineStat label="Retries" value={String(migration.retry_count)} />
                  <InlineStat label="SIS reads" value={String(migration.physical_sis_reads)} />
                  <InlineStat label="Duration" value={duration(migration.started_at_utc, migration.completed_at_utc)} />
                </div>
                <div className="mt-5 rounded-xl border border-white/7 bg-black/15 px-4 py-3">
                  <div className="flex items-center justify-between gap-3">
                    <span className="text-[10px] uppercase tracking-[0.13em] text-slate-600">Run ID</span>
                    <code className="truncate text-[10px] text-slate-400">{migration.run_id}</code>
                  </div>
                </div>
              </>
            ) : (
              <EmptyState title="Henüz migration çalıştırılmadı" detail="Önce dry-run ile planı inceleyebilir veya migration’ı doğrudan başlatabilirsiniz." />
            )}
          </div>

          <div className="panel rounded-2xl p-5 sm:p-6">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <div className="flex items-center gap-2">
                  <span className="flex h-6 w-6 items-center justify-center rounded-md bg-cyan-300/10 text-xs font-semibold text-cyan-300">4</span>
                  <h2 className="font-semibold text-slate-100">Target verification</h2>
                </div>
                <p className="mt-1 pl-8 text-xs text-slate-500">Logical/physical storage ve reconciliation</p>
              </div>
              {reconciliation && <Badge tone={reconciliation.is_reconciled ? "green" : "red"}>{reconciliation.is_reconciled ? "Reconciled" : "Mismatch"}</Badge>}
            </div>

            <div className="mt-6 rounded-xl border border-white/8 bg-black/10 p-4">
              <div className="flex items-end justify-between gap-4">
                <div><p className="text-[10px] uppercase tracking-[0.13em] text-slate-600">Logical data</p><p className="mt-1 font-mono text-lg text-slate-200">{bytes(target?.logical_bytes ?? 0)}</p></div>
                <div className="text-right"><p className="text-[10px] uppercase tracking-[0.13em] text-slate-600">Physical storage</p><p className="mt-1 font-mono text-lg text-cyan-300">{bytes(target?.physical_bytes ?? 0)}</p></div>
              </div>
              <div className="mt-4 h-2 overflow-hidden rounded-full bg-white/5">
                <div className="h-full rounded-full bg-gradient-to-r from-cyan-400 to-emerald-400 transition-all duration-700" style={{ width: `${physicalRatio}%` }} />
              </div>
              <div className="mt-2 flex justify-between text-[10px] text-slate-600"><span>{target?.unique_part_count ?? 0} unique SIS part</span><span>{target?.logical_bytes ? `${100 - physicalRatio}% storage saved` : "Awaiting migration"}</span></div>
            </div>

            {reconciliation && (
              <div className="mt-4 grid grid-cols-3 gap-3">
                <ResultMetric label="Expected" value={reconciliation.expected_item_count} />
                <ResultMetric label="Target" value={reconciliation.target_item_count} />
                <ResultMetric label="Matched" value={reconciliation.matched_item_count} tone={reconciliation.is_reconciled ? "green" : "red"} />
              </div>
            )}

            <button disabled={busy || !target?.item_count} onClick={reconcile} className="mt-4 inline-flex w-full items-center justify-center gap-2 rounded-xl border border-emerald-300/20 bg-emerald-300/[0.08] px-4 py-3 text-xs font-semibold text-emerald-300 transition hover:bg-emerald-300/[0.13] disabled:cursor-not-allowed disabled:opacity-40">
              <Icon name="shield" className="h-4 w-4" />
              {operation === "reconcile" ? "Doğrulanıyor…" : "Reconciliation çalıştır"}
            </button>
          </div>
        </section>

        <footer className="mt-8 flex flex-col justify-between gap-3 border-t border-white/8 pt-6 text-[10px] uppercase tracking-[0.13em] text-slate-600 sm:flex-row">
          <span>Enterprise Vault → storionX</span>
          <span>SHA-256 · idempotency · checkpoint · legal hold</span>
        </footer>
      </div>
    </main>
  );
}

function Metric({ label, value, tone = "default" }: { label: string; value: number | string; tone?: "default" | "cyan" | "green" }) {
  const color = tone === "cyan" ? "text-cyan-300" : tone === "green" ? "text-emerald-300" : "text-slate-100";
  return <div className="rounded-xl border border-white/8 bg-white/[0.025] px-3 py-3 sm:px-4"><p className="text-[9px] uppercase tracking-[0.13em] text-slate-600">{label}</p><p className={`mt-1.5 font-mono text-xl font-medium ${color}`}>{value}</p></div>;
}

function TopologyNode({ icon, eyebrow, title, detail, active = false }: { icon: "archive" | "engine" | "target"; eyebrow: string; title: string; detail: string; active?: boolean }) {
  return <div className={`rounded-xl border p-4 transition ${active ? "border-cyan-300/30 bg-cyan-300/[0.075]" : "border-white/8 bg-black/10"}`}><div className="flex items-center gap-3"><span className={`flex h-10 w-10 items-center justify-center rounded-lg ${active ? "working-dot bg-cyan-300/15 text-cyan-300" : "bg-white/5 text-slate-400"}`}><Icon name={icon} /></span><div><p className="text-[9px] uppercase tracking-[0.15em] text-slate-600">{eyebrow}</p><p className="mt-0.5 text-sm font-semibold text-slate-200">{title}</p><p className="mt-0.5 text-[10px] text-slate-500">{detail}</p></div></div></div>;
}

function Badge({ children, tone }: { children: React.ReactNode; tone: "green" | "amber" | "red" | "blue" }) {
  const styles = { green: "border-emerald-400/20 bg-emerald-400/[0.08] text-emerald-300", amber: "border-amber-300/20 bg-amber-300/[0.08] text-amber-200", red: "border-rose-400/20 bg-rose-400/[0.08] text-rose-300", blue: "border-cyan-300/20 bg-cyan-300/[0.08] text-cyan-300" };
  return <span className={`inline-flex rounded-full border px-2.5 py-1 text-[10px] font-medium ${styles[tone]}`}>{children}</span>;
}

function ResultMetric({ label, value, tone = "default" }: { label: string; value: number; tone?: "default" | "green" | "cyan" | "amber" | "red" }) {
  const styles = { default: "text-slate-200", green: "text-emerald-300", cyan: "text-cyan-300", amber: "text-amber-200", red: "text-rose-300" };
  return <div className="rounded-xl border border-white/7 bg-black/10 px-3 py-3"><p className="text-[9px] uppercase tracking-[0.12em] text-slate-600">{label}</p><p className={`mt-1.5 font-mono text-lg ${styles[tone]}`}>{value}</p></div>;
}

function InlineStat({ label, value }: { label: string; value: string }) {
  return <div><p className="text-[9px] uppercase tracking-[0.12em] text-slate-600">{label}</p><p className="mt-1 font-mono text-xs text-slate-300">{value}</p></div>;
}

function Field({ label, value, onChange, placeholder }: { label: string; value: string; onChange: (value: string) => void; placeholder: string }) {
  return <label className="block"><span className="mb-1.5 block text-[10px] uppercase tracking-[0.12em] text-slate-600">{label}</span><input value={value} onChange={(event) => onChange(event.target.value)} placeholder={placeholder} className="w-full rounded-lg border border-white/8 bg-black/15 px-3 py-2 text-xs text-slate-200 placeholder:text-slate-700" /></label>;
}

function EmptyState({ title, detail }: { title: string; detail: string }) {
  return <div className="mt-6 rounded-xl border border-dashed border-white/10 px-6 py-10 text-center"><span className="mx-auto flex h-9 w-9 items-center justify-center rounded-full bg-white/5 text-slate-600"><Icon name="check" className="h-4 w-4" /></span><p className="mt-3 text-xs font-medium text-slate-400">{title}</p><p className="mx-auto mt-1 max-w-sm text-[10px] leading-4 text-slate-600">{detail}</p></div>;
}
