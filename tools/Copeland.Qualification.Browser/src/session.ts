import { chromium, type Page } from "playwright";
import { startServer } from "./server.ts";

export type { Page } from "playwright";

export async function createBrowserSession(directory: URL) {
    const server = await startServer(directory);
    try {
        const browser = await chromium.launch({
            channel: process.env.FRICTION_BROWSER_CHANNEL ?? "msedge",
        });
        const errors: string[] = [];
        return {
            browser,
            errors,
            async openPage(profile: string, width: number): Promise<Page> {
                const page = await browser.newPage({ viewport: { width, height: 960 } });
                const label = `${profile}/${width}`;
                page.on("pageerror", error => errors.push(`${label}: ${error.stack ?? error.message}`));
                page.on("console", message => {
                    if (message.type() === "error" || message.type() === "warning") {
                        errors.push(`${label}: ${message.text()}`);
                    }
                });
                page.on("response", response => {
                    if (response.status() >= 400) {
                        errors.push(`${label}: HTTP ${response.status()} ${response.url()}`);
                    }
                });
                await page.goto(`${server.url}/${profile}/index.html`);
                return page;
            },
            async close(): Promise<void> {
                try {
                    await browser.close();
                } finally {
                    await server.close();
                }
            },
        };
    } catch (error) {
        await server.close();
        throw error;
    }
}
