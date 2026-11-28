import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { resolve } from "node:path";

const [url, desktopSavePath, modulesRoot] = process.argv.slice(2);
if (!url || !desktopSavePath || !modulesRoot) {
    throw new Error("Usage: node scripts/check-web-proof.mjs <url> <desktop-save> <playwright-root>");
}
const require = createRequire(resolve(modulesRoot, "package.json"));
const { chromium } = require("playwright");
const normalizeLines = text => text.replaceAll("\r\n", "\n");
const desktop = normalizeLines(readFileSync(desktopSavePath, "utf8"));
const browser = await chromium.launch();

try {
    const page = await browser.newPage();
    const errors = [];
    page.on("pageerror", error => errors.push(error.message));
    await page.goto(url);
    await page.getByRole("button", { name: "Move army" }).waitFor({ timeout: 60000 });
    assert.match(await page.locator("#army").textContent(), /army-rome \(100, 105\)/);
    await page.waitForFunction(() =>
        document.querySelector("canvas").getContext("2d").getImageData(0, 0, 1, 1).data[3] === 255);

    await page.locator("#destination-x").fill("101");
    await page.locator("#destination-x").press("Tab");
    await page.getByRole("button", { name: "Move army" }).click();
    await page.waitForFunction(() => document.querySelector("#result").textContent.includes("moved from (100,105) to (101,105)"));
    const initialTurn = Number(await page.locator("#turn").textContent());
    await page.getByRole("button", { name: "End turn" }).click();
    await page.waitForFunction(turn => Number(document.querySelector("#turn").textContent) > turn, initialTurn);

    await page.getByRole("button", { name: "Save", exact: true }).click();
    await page.getByText("Saved in this browser.", { exact: true }).waitFor();
    const readSave = async () => normalizeLines(await page.evaluate(() => localStorage.getItem("ic2.web.proof.italy-500bc")));
    assert.ok(await readSave() === desktop, "browser and native CLI wrote the same canonical save");

    await page.reload();
    await page.getByRole("button", { name: "Reload save" }).click();
    await page.getByText("Saved campaign reloaded.", { exact: true }).waitFor();
    await page.getByRole("button", { name: "Save", exact: true }).click();
    await page.getByText("Saved in this browser.", { exact: true }).waitFor();
    assert.ok(await readSave() === desktop, "reloaded browser game preserved the desktop save state");
    assert.deepEqual(errors, [], "no browser errors");
    console.log("Browser and desktop matched after movement, end turn, save and reload.");
} catch (error) {
    console.error(await browser.contexts()[0]?.pages()[0]?.locator("#app").textContent());
    throw error;
} finally {
    await browser.close();
}
