// Wrapper: ruft fspackagetool.exe robust auf (cmd-Quoting mit Leerzeichen funktioniert nicht zuverlaessig).
const { spawnSync } = require("child_process");
const path = require("path");
const fs = require("fs");

const sdkRoot = process.env.MSFS_SDK || "C:\\MSFS 2024 SDK";
const tool = path.join(sdkRoot, "Tools", "bin", "fspackagetool.exe");
const projectXml = path.resolve(__dirname, "..", "..", "..", "GsimulationsKneeboardProject.xml");

if (!fs.existsSync(tool)) {
  console.error(`fspackagetool.exe nicht gefunden: ${tool}`);
  console.error(`Setze MSFS_SDK Umgebungsvariable falls dein SDK an einem anderen Pfad liegt.`);
  process.exit(1);
}
if (!fs.existsSync(projectXml)) {
  console.error(`Project XML nicht gefunden: ${projectXml}`);
  process.exit(1);
}

console.log(`Building MSFS package via ${tool}`);
const result = spawnSync(tool, [projectXml, "-rebuild", "-nopause"], { stdio: "inherit" });
process.exit(result.status || 0);
