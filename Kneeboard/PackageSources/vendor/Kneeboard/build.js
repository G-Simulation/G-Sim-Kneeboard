const copyStaticFiles = require("esbuild-copy-static-files");
const globalExternals = require("@fal-works/esbuild-plugin-global-externals");
const { typecheckPlugin } = require("@jgoz/esbuild-plugin-typecheck");
const esbuild = require("esbuild");
const postcss = require("postcss");
const postCssUrl = require("postcss-url");
const postcssPrefixSelector = require("postcss-prefix-selector");
const sassPlugin = require("esbuild-sass-plugin");
const fs = require("fs");
const path = require("path");

require("dotenv").config({ path: __dirname + "/.env" });

const env = {
  typechecking: process.env.TYPECHECKING === "true",
  sourcemaps: process.env.SOURCE_MAPS === "true",
  minify: process.env.MINIFY === "true",
};
const rawAppId = process.env.APP_ID || "Kneeboard";
const appViewClass = process.env.APP_VIEW_CLASS || "GsimKneeboard";
const apiProxyUrl = process.env.API_PROXY_URL || "http://localhost:815";
const baseUrl = `coui://html_ui/efb_ui/efb_apps/${rawAppId}`;

const baseConfig = {
  entryPoints: ["src/GsimKneeboard.tsx"],
  keepNames: true,
  bundle: true,
  outdir: "dist",
  sourcemap: env.sourcemaps,
  minify: env.minify,
  logLevel: "debug",
  target: "es2017",
  define: {
    BASE_URL: `"${baseUrl}"`,
    API_PROXY_URL: `"${apiProxyUrl}"`,
  },
  plugins: [
    copyStaticFiles({
      src: "./src/Assets",
      dest: "./dist/Assets",
      filter(srcPath) {
        try {
          if (!fs.existsSync(srcPath)) {
            return false;
          }
          if (fs.statSync(srcPath).isDirectory()) {
            return true;
          }
          const allowedExtensions = new Set([
            ".html",
            ".htm",
            ".css",
            ".js",
            ".svg",
            ".png",
            ".jpg",
            ".jpeg",
            ".ico",
            ".txt",
            ".json",
            ".mjs",
            ".map",
          ]);
          return allowedExtensions.has(path.extname(srcPath).toLowerCase());
        } catch (error) {
          console.warn("copy filter error", error);
          return false;
        }
      },
    }),
    convertUtf16JsonPlugin("dist/Assets"),
    copyToolbarAssetsPlugin(),
    globalExternals.globalExternals({
      "@microsoft/msfs-sdk": {
        varName: "msfssdk",
        type: "cjs",
      },
      "@workingtitlesim/garminsdk": {
        varName: "garminsdk",
        type: "cjs",
      },
    }),
    sassPlugin.sassPlugin({
      async transform(source) {
        const { css } = await postcss([
          postCssUrl({
            url: "copy",
          }),
          postcssPrefixSelector({
            prefix: `.efb-view.${appViewClass}`,
          }),
        ]).process(source, { from: undefined });
        return css;
      },
    }),
  ],
};

function convertUtf16JsonPlugin(relativeAssetsPath) {
  return {
    name: "convert-utf16-json",
    setup(build) {
      build.onEnd(() => {
        const assetsDir = path.resolve(__dirname, relativeAssetsPath);
        convertJsonFiles(assetsDir);
      });
    },
  };
}

function copyToolbarAssetsPlugin() {
  return {
    name: "copy-toolbar-assets",
    setup(build) {
      build.onEnd(() => {
        copyToolbarAssets();
      });
    },
  };
}

function convertJsonFiles(dirPath) {
  if (!fs.existsSync(dirPath)) {
    return;
  }

  const entries = fs.readdirSync(dirPath, { withFileTypes: true });
  for (const entry of entries) {
    const entryPath = path.join(dirPath, entry.name);
    if (entry.isDirectory()) {
      convertJsonFiles(entryPath);
      continue;
    }
    if (path.extname(entry.name).toLowerCase() !== ".json") {
      continue;
    }

    try {
      const buffer = fs.readFileSync(entryPath);
      if (buffer.length < 2) {
        continue;
      }
      let encoding = "utf8";
      if (buffer[0] === 0xff && buffer[1] === 0xfe) {
        encoding = "utf16le";
      } else if (buffer[0] === 0xfe && buffer[1] === 0xff) {
        encoding = "utf16be";
      }

      if (encoding === "utf8") {
        const text = buffer.toString("utf8");
        if (text.charCodeAt(0) === 0xfeff) {
          fs.writeFileSync(entryPath, text.slice(1), "utf8");
        }
        continue;
      }

      const text = buffer.toString(encoding);
      fs.writeFileSync(entryPath, text, "utf8");
    } catch (error) {
      console.warn("Failed to normalize JSON encoding:", entryPath, error);
    }
  }
}

function ensureDirExists(targetDir) {
  if (!fs.existsSync(targetDir)) {
    fs.mkdirSync(targetDir, { recursive: true });
  }
}

function copyFolderRecursive(src, dest) {
  if (!fs.existsSync(src)) {
    return;
  }
  ensureDirExists(dest);
  fs.cpSync(src, dest, { recursive: true, force: true });
}

function copyToolbarAssets() {
  const srcHtmlUi = path.join(__dirname, "toolbar", "html_ui");
  const srcPanels = path.join(__dirname, "toolbar", "InGamePanels");
  const distHtmlUi = path.join(__dirname, "dist", "html_ui");
  const distPanels = path.join(__dirname, "dist", "InGamePanels");

  copyFolderRecursive(srcHtmlUi, distHtmlUi);
  copyFolderRecursive(srcPanels, distPanels);

  // Ensure toolbar has logo and icon even if missing from source tree
  const logoSrc = path.join(__dirname, "src", "Assets", "Logo.png");
  const logoDest = path.join(
    distHtmlUi,
    "InGamePanels",
    "Kneeboard",
    "Assets",
    "Logo.png"
  );
  if (fs.existsSync(logoSrc)) {
    ensureDirExists(path.dirname(logoDest));
    fs.copyFileSync(logoSrc, logoDest);
  }

  const toolbarIconSrc = path.join(
    __dirname,
    "toolbar",
    "html_ui",
    "icons",
    "toolbar",
    "ICON_TOOLBAR_KNEEBOARD.svg"
  );
  const appIconSrc = path.join(__dirname, "src", "Assets", "app-icon.svg");
  const iconSrc = fs.existsSync(toolbarIconSrc) ? toolbarIconSrc : appIconSrc;
  const iconDest = path.join(
    distHtmlUi,
    "icons",
    "toolbar",
    "ICON_TOOLBAR_KNEEBOARD.svg"
  );
  const iconDestCapitalized = path.join(
    distHtmlUi,
    "Icons",
    "Toolbar",
    "ICON_TOOLBAR_KNEEBOARD.svg"
  );
  if (fs.existsSync(iconSrc)) {
    ensureDirExists(path.dirname(iconDest));
    fs.copyFileSync(iconSrc, iconDest);
    ensureDirExists(path.dirname(iconDestCapitalized));
    fs.copyFileSync(iconSrc, iconDestCapitalized);
  }
}

if (env.typechecking) {
  baseConfig.plugins.push(
    typecheckPlugin({ watch: process.env.SERVING_MODE === "WATCH" })
  );
}
if (process.env.SERVING_MODE === "WATCH") {
  esbuild.context(baseConfig).then((ctx) => {
    return ctx.watch();
  });
} else if (process.env.SERVING_MODE === "SERVE") {
  esbuild
    .context(baseConfig)
    .then((ctx) => {
      return ctx.serve({ port: process.env.PORT_SERVER });
    });
} else if (["", undefined].includes(process.env.SERVING_MODE)) {
  esbuild.build(baseConfig);
} else {
  console.error(`MODE ${process.env.SERVING_MODE} is unknown`);
}
