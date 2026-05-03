import * as duckdb from "@duckdb/duckdb-wasm";
import duckdbMvpWorker from "@duckdb/duckdb-wasm/dist/duckdb-browser-mvp.worker.js?url";
import duckdbMvpWasm from "@duckdb/duckdb-wasm/dist/duckdb-mvp.wasm?url";
import duckdbEhWorker from "@duckdb/duckdb-wasm/dist/duckdb-browser-eh.worker.js?url";
import duckdbEhWasm from "@duckdb/duckdb-wasm/dist/duckdb-eh.wasm?url";
import type { Table } from "apache-arrow";

const bundles: duckdb.DuckDBBundles = {
  mvp: {
    mainModule: duckdbMvpWasm,
    mainWorker: duckdbMvpWorker
  },
  eh: {
    mainModule: duckdbEhWasm,
    mainWorker: duckdbEhWorker
  }
};

let clientPromise: Promise<DuckDbClient> | null = null;

export class DuckDbClient {
  readonly db: duckdb.AsyncDuckDB;
  readonly conn: duckdb.AsyncDuckDBConnection;
  private registered = new WeakMap<File, string>();
  private registeredUrls = new Map<string, string>();
  private registeredNames = new Set<string>();
  private nextFileId = 1;

  private constructor(db: duckdb.AsyncDuckDB, conn: duckdb.AsyncDuckDBConnection) {
    this.db = db;
    this.conn = conn;
  }

  static async create(): Promise<DuckDbClient> {
    const bundle = await duckdb.selectBundle(bundles);
    if (!bundle.mainWorker) {
      throw new Error("DuckDB-WASM did not provide a browser worker bundle.");
    }

    const worker = new Worker(bundle.mainWorker);
    const logger = new duckdb.ConsoleLogger(duckdb.LogLevel.WARNING);
    const db = new duckdb.AsyncDuckDB(logger, worker);
    await db.instantiate(bundle.mainModule, bundle.pthreadWorker);
    const conn = await db.connect();

    await optionalPragma(conn, "PRAGMA threads=4");
    await optionalPragma(conn, "PRAGMA memory_limit='3GB'");

    return new DuckDbClient(db, conn);
  }

  async registerFile(file: File, hint: string): Promise<string> {
    const existing = this.registered.get(file);
    if (existing) {
      return existing;
    }

    const name = this.uniqueName(hint);
    await this.db.registerFileHandle(
      name,
      file,
      duckdb.DuckDBDataProtocol.BROWSER_FILEREADER,
      true
    );
    this.registered.set(file, name);
    return name;
  }

  async registerUrl(url: string, hint: string): Promise<string> {
    const existing = this.registeredUrls.get(url);
    if (existing) {
      return existing;
    }

    const name = this.uniqueName(hint);
    await this.db.registerFileURL(name, url, duckdb.DuckDBDataProtocol.HTTP, false);
    this.registeredUrls.set(url, name);
    return name;
  }

  async query<T extends object>(sql: string): Promise<T[]> {
    const result = await this.conn.query(sql);
    return tableToObjects<T>(result);
  }

  parquet(name: string): string {
    return `read_parquet('${escapeSql(name)}', hive_partitioning=false)`;
  }

  close(): void {
    void this.conn.close();
  }

  private uniqueName(hint: string): string {
    const cleanHint = hint
      .replaceAll("\\", "/")
      .replace(/[^a-zA-Z0-9_.-]+/g, "_")
      .replace(/^_+|_+$/g, "")
      .slice(-90);

    let candidate = `trace_${this.nextFileId++}_${cleanHint || "file.parquet"}`;
    while (this.registeredNames.has(candidate)) {
      candidate = `trace_${this.nextFileId++}_${cleanHint || "file.parquet"}`;
    }

    this.registeredNames.add(candidate);
    return candidate;
  }
}

export function getDuckDbClient(): Promise<DuckDbClient> {
  clientPromise ??= DuckDbClient.create();
  return clientPromise;
}

export function escapeSql(value: string): string {
  return value.replaceAll("'", "''");
}

function tableToObjects<T extends object>(table: Table): T[] {
  const fields = table.schema.fields.map((field) => field.name);
  const columns = fields.map((field) => table.getChild(field));
  const rows: T[] = [];

  for (let rowIndex = 0; rowIndex < table.numRows; rowIndex++) {
    const row: Record<string, unknown> = {};
    for (let columnIndex = 0; columnIndex < fields.length; columnIndex++) {
      const value = columns[columnIndex]?.get(rowIndex);
      row[fields[columnIndex]] = normalizeValue(value);
    }

    rows.push(row as T);
  }

  return rows;
}

function normalizeValue(value: unknown): unknown {
  if (typeof value === "bigint") {
    return Number(value);
  }

  if (value instanceof Uint8Array) {
    return new TextDecoder().decode(value);
  }

  return value;
}

async function optionalPragma(conn: duckdb.AsyncDuckDBConnection, sql: string): Promise<void> {
  try {
    await conn.query(sql);
  } catch {
    // Some browser bundles do not expose the same runtime knobs. Query execution still works.
  }
}
