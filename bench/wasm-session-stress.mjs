// wasm-session-stress.mjs — playground の wasm (AOT) で session API を node から繰り返し叩く。
// lub の web 検証 (verify-headless.mjs) の A5 (全サンプルを順に Open) → A6 (17_flappy を
// Open して body edit を Update) と同じ呼び出し列を、ブラウザ無しで再現する。
// #385 (Mono wasm AOT の GC root 漏れで BuildObjectCreation が null function / NRE になる) の
// 再現と修正確認に使った。修正前は 4 プロセス全部が 10 回未満の Open で落ちた。
//
// 前提: WasmCompiler を AOT で publish 済み (lub の gen-tcs-assets.mjs --publish と同じ):
//   dotnet publish WasmCompiler -c Release -p:RunAOTCompilation=true -p:CompressionEnabled=false
// 実行 (tcs の root から):
//   node bench/wasm-session-stress.mjs
//   ITER=5 ORDER=a5 node bench/wasm-session-stress.mjs   # 全サンプル → flappy を 5 周
//   ORDER=flappy node bench/wasm-session-stress.mjs      # flappy だけ
// 環境変数: LUB_REPO (default ../lub)、FRAMEWORK (publish の _framework)、ITER、ORDER (a5 | flappy)
import { readFileSync, readdirSync, statSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const TCS = resolve(HERE, "..");
const LUB = resolve(process.env.LUB_REPO ?? join(TCS, "..", "lub"));
const FRAMEWORK = resolve(
  process.env.FRAMEWORK ??
    join(TCS, "WasmCompiler/bin/Release/net10.0/publish/wwwroot/_framework"),
);
const ITER = Number(process.env.ITER ?? 1);
const ORDER = process.env.ORDER ?? "a5";

function walk(dir, base = dir, out = {}) {
  for (const n of readdirSync(dir)) {
    const p = join(dir, n);
    if (statSync(p).isDirectory()) walk(p, base, out);
    else if (n.endsWith(".cs"))
      out[p.slice(base.length + 1)] = readFileSync(p, "utf8");
  }
  return out;
}
// playground の worker と同じ: cs-lib の実装を全部入れ、lub_stub.cs は --ref 相当
const impl = walk(join(LUB, "cs-lib"));
const stub = impl["lub_stub.cs"];
delete impl["lub_stub.cs"];
// サンプル一覧の正は web/playground/samples.ts の CS_SAMPLES
const samplesTs = readFileSync(join(LUB, "web/playground/samples.ts"), "utf8");
const block = samplesTs.match(/CS_SAMPLES[^{]*\{([^}]*)\}/)[1];
const samples = Object.fromEntries(
  [...block.matchAll(/"([^"]+)":\s*"([^"]+)"/g)].map((m) => [m[1], m[2]]),
);

const { dotnet } = await import(
  pathToFileURL(join(FRAMEWORK, "dotnet.js")).href
);
const runtime = await dotnet.withDiagnosticTracing(false).create();
const exports = await runtime.getAssemblyExports("WasmCompiler");
const S = exports.SessionExports;

function open(name) {
  const entry = samples[name];
  const src = readFileSync(join(LUB, "samples", name, entry + ".cs"), "utf8");
  const t0 = performance.now();
  const r = JSON.parse(
    S.Open(
      JSON.stringify({
        files: { ...impl, [entry + ".cs"]: src },
        refs: { "lub_stub.cs": stub },
        entryClass: entry,
      }),
    ),
  );
  const ms = Math.round(performance.now() - t0);
  if (!r.ok) throw new Error(`open ${name} failed: ${r.errors.join("\n")}`);
  const lua = S.LinkSnapshot(r.epoch);
  if (lua.startsWith("--@@tcs_error")) throw new Error(`link ${name}: ${lua}`);
  return { r, src, entry, ms };
}
function update(epoch, path, content) {
  const r = JSON.parse(S.Update(epoch, path, content));
  if (!r.ok) throw new Error(`update failed: ${r.errors.join("\n")}`);
  return r;
}

let step = "";
try {
  for (let it = 0; it < ITER; it++) {
    if (ORDER === "a5") {
      for (const name of Object.keys(samples)) {
        step = `open ${name}`;
        const { ms } = open(name);
        process.stdout.write(`${name}:${ms}ms `);
      }
      console.log();
    }
    step = "open 17_flappy";
    const { r, src, entry } = open("17_flappy");
    step = "update 17_flappy";
    const edited = src.replace("velocityY = 3.0f;", "velocityY = 3.25f;");
    if (edited === src) throw new Error("edit marker not found");
    const u = update(r.epoch, entry + ".cs", edited);
    const lua = S.LinkSnapshot(r.epoch);
    if (lua.startsWith("--@@tcs_error")) throw new Error(lua);
    console.log(`iter ${it}: update fast=${u.fastPath} rev=${u.revision} ok`);
  }
  console.log("OK");
  process.exit(0);
} catch (e) {
  console.log(`FAIL at ${step}: ${(e && e.stack) || e}`);
  process.exit(1);
}
